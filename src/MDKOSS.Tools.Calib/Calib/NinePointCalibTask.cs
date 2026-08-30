using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Tools.Calib.Calib;

/// <summary>
/// 3×3 grid on a platform. Records commanded vs observed (vision or encoder) and
/// writes mean offset / residual. Works on sim without a real camera.
/// </summary>
public sealed class NinePointCalibTask : CalibMotionTaskBase
{
    private readonly List<(double X, double Y, double Mx, double My)> _samples = [];
    private int _index;
    private int _dwell;
    private bool _enabled;

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
        SetResult("ok", false);
        SetResult("offsetX", 0);
        SetResult("offsetY", 0);
        SetResult("residual", 0);
        SetResult("points", 0);
        SetResult("message", "");
    }

    protected override void Step()
    {
        if (_dwell > 0)
        {
            _dwell--;
            return;
        }

        var platformId = ReadString("platformDeviceId", "platform-xy");
        if (!TryGetPlatformDevice(platformId, out var platform) || platform is null)
        {
            throw new InvalidOperationException($"平台 '{platformId}' 不存在");
        }

        if (!_enabled)
        {
            if (!PlatformStartMotion(platformId))
            {
                throw new InvalidOperationException($"使能平台 '{platformId}' 失败");
            }

            _enabled = true;
            Enter(CalibPhase.Running, $"使能 {platformId}");
            _dwell = 1;
            return;
        }

        var points = BuildGrid();
        if (_index >= points.Count)
        {
            Finish(platformId);
            return;
        }

        var (x, y) = points[_index];
        if (!PlatformAxisMoveTo(platformId, "X", x) || !PlatformAxisMoveTo(platformId, "Y", y))
        {
            throw new InvalidOperationException($"九点第 {_index + 1} 点运动失败");
        }

        var observedX = ReadObserved("X", platform, x);
        var observedY = ReadObserved("Y", platform, y);
        _samples.Add((x, y, observedX, observedY));
        SetResult("points", _samples.Count);
        Enter(CalibPhase.Running, $"点 {_index + 1}/{points.Count}  cmd=({x:F2},{y:F2}) obs=({observedX:F2},{observedY:F2})");

        _index++;
        _dwell = Math.Max(1, ReadInt("settleTicks", 2));
    }

    protected override void OnStop()
    {
        var platformId = ReadString("platformDeviceId", "platform-xy");
        PlatformStopMotion(platformId);
        _enabled = false;
    }

    private void Finish(string platformId)
    {
        if (_samples.Count == 0)
        {
            throw new InvalidOperationException("没有采样点");
        }

        var ox = _samples.Average(s => s.Mx - s.X);
        var oy = _samples.Average(s => s.My - s.Y);
        var residual = Math.Sqrt(_samples.Average(s =>
        {
            var dx = s.Mx - s.X - ox;
            var dy = s.My - s.Y - oy;
            return dx * dx + dy * dy;
        }));

        SetResult("offsetX", ox);
        SetResult("offsetY", oy);
        SetResult("residual", residual);
        SetResult("points", _samples.Count);
        SetResult("ok", residual <= ReadDouble("maxResidual", 0.5));
        SetResult("message", $"dX={ox:F4} dY={oy:F4} residual={residual:F4}");
        PlatformStopMotion(platformId);
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

    private double ReadObserved(string axis, PlatformDevice platform, double commanded)
    {
        // Optional vision overlay (pixel → mm already applied by host vars).
        var visionKey = axis.Equals("X", StringComparison.OrdinalIgnoreCase) ? "vision.x" : "vision.y";
        if (TryGetGlobalVar<double>(visionKey, out var vision) && Math.Abs(vision) > 1e-12)
        {
            return commanded + vision;
        }

        var entry = platform.Axes.FirstOrDefault(a =>
            string.Equals(a.AxisLetter, axis, StringComparison.OrdinalIgnoreCase));
        if (entry?.Axis is not null)
        {
            var pos = ReadAxisPosition(entry.Axis);
            if (Math.Abs(pos) > 1e-12 || Math.Abs(commanded) < 1e-12)
            {
                return pos;
            }
        }

        return commanded;
    }
}
