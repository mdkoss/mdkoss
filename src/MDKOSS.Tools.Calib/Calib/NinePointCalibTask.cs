using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Tools.Calib.Calib;

/// <summary>
/// Nine-point calibration: move the platform on a 3×3 grid, read feature pixel
/// coordinates from the bound camera/vision vars, and fit a pixel→platform matrix
/// (rigid / affine / perspective). Works on sim with synthetic pixels when vision is idle.
/// </summary>
public sealed class NinePointCalibTask : CalibMotionTaskBase
{
    private readonly List<CalibTransform.PointPair> _samples = [];
    private int _index;
    private int _dwell;
    private bool _enabled;
    private string _platformId = "";
    private string _cameraId = "";

    public NinePointCalibTask(
        string name,
        int intervalMs,
        IDriver driver,
        MVarStore vars,
        IReadOnlyDictionary<string, MDeviceBase> devices,
        IReadOnlyDictionary<string, string>? parameters = null,
        MdkAlarmManager? alarms = null)
        : base(name, intervalMs, driver, vars, devices, parameters, alarms)
    {
    }

    protected override void OnStart()
    {
        _samples.Clear();
        _index = 0;
        _dwell = 0;
        _enabled = false;
        _platformId = RequireDeviceId("platformDeviceId", "平台");
        _cameraId = RequireDeviceId("cameraDeviceId", "相机");

        if (!TryGetPlatformDevice(_platformId, out var platform) || platform is null)
        {
            throw new InvalidOperationException($"平台 '{_platformId}' 不存在");
        }

        if (!TryGetDevice<CameraDevDevice>(_cameraId, out var camera) || camera is null)
        {
            throw new InvalidOperationException($"相机 '{_cameraId}' 不存在");
        }

        SetResult("ok", false);
        SetResult("platformDeviceId", _platformId);
        SetResult("cameraDeviceId", _cameraId);
        SetResult("transformMode", ReadString("transformMode", "affine"));
        SetResult("matrix", "");
        SetResult("offsetX", 0);
        SetResult("offsetY", 0);
        SetResult("residual", 0);
        SetResult("points", 0);
        SetResult("message", "");
        ClearMatrixCells();
    }

    protected override void Step()
    {
        if (_dwell > 0)
        {
            _dwell--;
            return;
        }

        if (!TryGetPlatformDevice(_platformId, out var platform) || platform is null)
        {
            throw new InvalidOperationException($"平台 '{_platformId}' 不存在");
        }

        if (!_enabled)
        {
            if (!PlatformStartMotion(_platformId))
            {
                throw new InvalidOperationException($"使能平台 '{_platformId}' 失败");
            }

            _enabled = true;
            Enter(CalibPhase.Running, $"使能平台 {_platformId} / 相机 {_cameraId}");
            _dwell = 1;
            return;
        }

        var points = BuildGrid();
        if (_index >= points.Count)
        {
            Finish();
            return;
        }

        var (x, y) = points[_index];
        if (!PlatformAxisMoveTo(_platformId, "X", x) || !PlatformAxisMoveTo(_platformId, "Y", y))
        {
            throw new InvalidOperationException($"九点第 {_index + 1} 点运动失败");
        }

        var (pixelX, pixelY) = ReadPixelFeature(x, y);
        _samples.Add(new CalibTransform.PointPair(pixelX, pixelY, x, y));
        SetResult("points", _samples.Count);
        Enter(
            CalibPhase.Running,
            $"点 {_index + 1}/{points.Count}  platform=({x:F2},{y:F2}) pixel=({pixelX:F2},{pixelY:F2})");

        _index++;
        _dwell = Math.Max(1, ReadInt("settleTicks", 2));
    }

    protected override void OnStop()
    {
        if (!string.IsNullOrWhiteSpace(_platformId))
        {
            PlatformStopMotion(_platformId);
        }

        _enabled = false;
    }

    private void Finish()
    {
        if (_samples.Count == 0)
        {
            throw new InvalidOperationException("没有采样点");
        }

        var mode = CalibTransform.ParseMode(ReadString("transformMode", "affine"));
        var fit = CalibTransform.Fit(_samples, mode);
        var m = fit.Matrix;
        var matrixText = fit.ToMatrixString();

        SetResult("transformMode", mode.ToString().ToLowerInvariant());
        SetResult("matrix", matrixText);
        SetResult("m00", m[0, 0]);
        SetResult("m01", m[0, 1]);
        SetResult("m02", m[0, 2]);
        SetResult("m10", m[1, 0]);
        SetResult("m11", m[1, 1]);
        SetResult("m12", m[1, 2]);
        SetResult("m20", m[2, 0]);
        SetResult("m21", m[2, 1]);
        SetResult("m22", m[2, 2]);
        SetResult("offsetX", m[0, 2]);
        SetResult("offsetY", m[1, 2]);
        SetResult("residual", fit.Residual);
        SetResult("points", _samples.Count);
        SetResult("platformDeviceId", _platformId);
        SetResult("cameraDeviceId", _cameraId);
        SetResult("ok", fit.Residual <= ReadDouble("maxResidual", 0.5));
        SetResult("message", $"{mode.ToString().ToLowerInvariant()} residual={fit.Residual:F4} matrix={matrixText}");

        PlatformStopMotion(_platformId);
        _enabled = false;
        Enter(CalibPhase.Done, "九点标定完成");
    }

    private List<(double X, double Y)> BuildGrid()
    {
        var originX = ReadDouble("originX", 0);
        var originY = ReadDouble("originY", 0);
        var pitch = ReadDouble("pitch", 5);
        var list = new List<(double, double)>(9);
        for (var r = -1; r <= 1; r++)
        {
            for (var c = -1; c <= 1; c++)
            {
                list.Add((originX + c * pitch, originY + r * pitch));
            }
        }

        return list;
    }

    /// <summary>
    /// Prefer live vision pose vars for the bound camera; otherwise synthesize pixels
    /// from platform pose so sim / dry-run still yields a solvable matrix.
    /// </summary>
    private (double X, double Y) ReadPixelFeature(double platformX, double platformY)
    {
        var prefix = ReadString("resultPrefix", "vision");
        if (TryReadFeature(prefix, out var px, out var py))
        {
            return (px, py);
        }

        if (TryReadFeature($"camera.{_cameraId}", out px, out py)
            || TryReadFeature($"device.{_cameraId}.feature", out px, out py))
        {
            return (px, py);
        }

        // Sim fallback: linear map platform→pixel (identity scale by default).
        var scale = ReadDouble("pixelScale", 10);
        var ox = ReadDouble("pixelOffsetX", 0);
        var oy = ReadDouble("pixelOffsetY", 0);
        return (platformX * scale + ox, platformY * scale + oy);
    }

    private bool TryReadFeature(string prefix, out double x, out double y)
    {
        x = 0;
        y = 0;
        var hasOk = TryGetGlobalVar<bool>($"{prefix}.ok", out var okFlag) && okFlag == true;
        if (!TryGetGlobalVar<double>($"{prefix}.x", out var vx) || !TryGetGlobalVar<double>($"{prefix}.y", out var vy))
        {
            return false;
        }

        x = vx;
        y = vy;
        // Accept explicit vision.ok, or any non-zero pose (legacy overlay).
        return hasOk || Math.Abs(x) > 1e-12 || Math.Abs(y) > 1e-12;
    }

    private string RequireDeviceId(string key, string label)
    {
        var id = ReadString(key, "");
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException($"九点标定必须指定{label}参数 {key}");
        }

        return id.Trim();
    }

    private void ClearMatrixCells()
    {
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                SetResult($"m{r}{c}", r == c ? 1 : 0);
            }
        }
    }
}
