using System.Text.Json;
using MDKOSS.Core.Drivers;
using MDKOSS.Core.Vision;
using OpenCvSharp;

namespace MDKOSS.Core;

/// <summary>Config parameters for <see cref="VisionDevice"/> (type <c>visiondev</c>).</summary>
public sealed class VisionDeviceParameters
{
    public string VisionId { get; init; } = "";
    public string CameraDeviceId { get; init; } = "";
    public string ImagePath { get; init; } = "";
    public string ResultPrefix { get; init; } = "vision";
    public string DebugImagePath { get; init; } = "";
    public bool GenerateTestImageWhenMissing { get; init; } = true;

    public static VisionDeviceParameters Parse(IReadOnlyDictionary<string, string>? parameters)
    {
        parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new VisionDeviceParameters
        {
            VisionId = Get(parameters, "visionId", "visionId"),
            CameraDeviceId = Get(parameters, "cameraDeviceId", "cameraId"),
            ImagePath = Get(parameters, "imagePath", "image"),
            ResultPrefix = Get(parameters, "resultPrefix", "prefix") is { Length: > 0 } p ? p : "vision",
            DebugImagePath = Get(parameters, "debugImagePath", "debugImage"),
            GenerateTestImageWhenMissing = ParseBool(Get(parameters, "generateTestImage", "autoImage"), true),
        };
    }

    private static string Get(IReadOnlyDictionary<string, string> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (map.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
            {
                return v.Trim();
            }
        }

        return "";
    }

    private static bool ParseBool(string raw, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "y" => true,
            "0" or "false" or "no" or "n" => false,
            _ => bool.TryParse(raw, out var b) ? b : fallback,
        };
    }
}

/// <summary>
/// Industrial vision device (config type <c>visiondev</c>).
/// Resolves a named <see cref="MdkSetting.VisionConfig"/> pipeline and runs it via <see cref="VisionExecutor"/>.
/// </summary>
public sealed class VisionDevice : MDeviceBase
{
    private readonly object _sync = new();
    private readonly Func<string, MdkSetting.VisionConfig?> _resolveVision;
    private readonly Func<string, MDeviceBase?> _resolveDevice;
    private readonly VisionExecutor _executor = new();
    private VisionRunResult? _lastResult;
    private string? _lastImagePath;
    private int _runCount;

    public VisionDevice(
        string id,
        string name,
        VisionDeviceParameters parameters,
        MVarStore vars,
        Func<string, MdkSetting.VisionConfig?> resolveVision,
        Func<string, MDeviceBase?> resolveDevice)
        : base(id, name, MDeviceType.Generic, new VisionLogicalDriver(), vars)
    {
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _resolveVision = resolveVision ?? throw new ArgumentNullException(nameof(resolveVision));
        _resolveDevice = resolveDevice ?? throw new ArgumentNullException(nameof(resolveDevice));
        PublishStatusVars();
    }

    public VisionDeviceParameters Parameters { get; private set; }

    public VisionRunResult? LastResult
    {
        get { lock (_sync) return _lastResult; }
    }

    public int RunCount
    {
        get { lock (_sync) return _runCount; }
    }

    /// <summary>Update parameters after config apply (same device instance).</summary>
    public void UpdateParameters(VisionDeviceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        lock (_sync)
        {
            Parameters = parameters;
            PublishStatusVarsUnlocked();
        }
    }

    public override void Start()
    {
        State = MDeviceState.Running;
        WriteState("running");
        PublishStatusVars();
    }

    public override DeviceSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new DeviceSnapshot(
                Id,
                Name,
                "visiondev",
                State.ToString(),
                "vision",
                true);
        }
    }

    /// <summary>
    /// Trigger camera (if configured), resolve input image, run vision pipeline, publish results.
    /// </summary>
    public VisionRunResult CaptureAndRun(string? imagePathOverride = null, string? visionIdOverride = null)
    {
        TriggerCameraCapture();
        var imagePath = ResolveImagePath(imagePathOverride);
        return Run(imagePath, visionIdOverride);
    }

    /// <summary>Run the configured (or overridden) vision pipeline on an image file.</summary>
    public VisionRunResult Run(string? imagePath = null, string? visionIdOverride = null)
    {
        lock (_sync)
        {
            var visionId = string.IsNullOrWhiteSpace(visionIdOverride)
                ? Parameters.VisionId
                : visionIdOverride.Trim();
            if (string.IsNullOrWhiteSpace(visionId))
            {
                return FailLocked("visionId_empty");
            }

            var cfg = _resolveVision(visionId);
            if (cfg is null)
            {
                return FailLocked($"vision_not_found:{visionId}");
            }

            var doc = cfg.Pipeline ?? VisionDocument.CreateBasicInspectPipeline();
            if (doc.Nodes.Count == 0)
            {
                doc = VisionDocument.CreateBasicInspectPipeline();
            }

            var input = string.IsNullOrWhiteSpace(imagePath)
                ? ResolveImagePath(null)
                : imagePath.Trim();
            if (string.IsNullOrWhiteSpace(input) || !File.Exists(input))
            {
                return FailLocked($"image_not_found:{input}");
            }

            var debugPath = string.IsNullOrWhiteSpace(Parameters.DebugImagePath)
                ? Path.Combine(Path.GetTempPath(), $"mdkoss-vision-{Id}-debug.png")
                : Parameters.DebugImagePath;

            var result = _executor.Run(doc, input, debugPath);
            _lastImagePath = input;
            _lastResult = result;
            _runCount++;
            PublishResultVarsUnlocked(result);
            PublishStatusVarsUnlocked();
            State = result.Ok ? MDeviceState.Running : MDeviceState.Fault;
            WriteState(State.ToString().ToLowerInvariant());
            return result;
        }
    }

    private VisionRunResult FailLocked(string error)
    {
        var result = new VisionRunResult { Ok = false, Error = error };
        _lastResult = result;
        PublishResultVarsUnlocked(result);
        PublishStatusVarsUnlocked();
        State = MDeviceState.Fault;
        WriteState("fault");
        return result;
    }

    private void TriggerCameraCapture()
    {
        var cameraId = Parameters.CameraDeviceId;
        if (string.IsNullOrWhiteSpace(cameraId))
        {
            return;
        }

        var device = _resolveDevice(cameraId);
        if (device is null)
        {
            Vars.Set(BuildVarKey("camera.error"), $"camera_not_found:{cameraId}");
            return;
        }

        try
        {
            switch (device)
            {
                case CameraDevDevice cam:
                    cam.TriggerCapture(Parameters.VisionId is { Length: > 0 } v ? v : "default");
                    Vars.Set(BuildVarKey("camera.triggered"), true);
                    break;
                default:
                {
                    // Extension cameras (extcamera) expose TriggerCapture via reflection-friendly actions.
                    var trigger = device.GetType().GetMethod("TriggerCapture", [typeof(string)]);
                    if (trigger is not null)
                    {
                        trigger.Invoke(device, [Parameters.VisionId is { Length: > 0 } id ? id : "default"]);
                        Vars.Set(BuildVarKey("camera.triggered"), true);
                    }
                    else
                    {
                        Vars.Set(BuildVarKey("camera.error"), "camera_no_trigger");
                    }

                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Vars.Set(BuildVarKey("camera.error"), ex.Message);
        }
    }

    private string ResolveImagePath(string? overridePath)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath.Trim();
        }

        if (!string.IsNullOrWhiteSpace(Parameters.ImagePath) && File.Exists(Parameters.ImagePath))
        {
            return Parameters.ImagePath;
        }

        // Camera may publish a path var after capture (future real SDK).
        var cameraId = Parameters.CameraDeviceId;
        if (!string.IsNullOrWhiteSpace(cameraId))
        {
            var cam = _resolveDevice(cameraId);
            if (cam is not null)
            {
                var key = $"device.{cam.Name}.{cam.Id}.lastImagePath";
                if (Vars.TryGet<object>(key, out var pathObj)
                    && pathObj is string path
                    && !string.IsNullOrWhiteSpace(path)
                    && File.Exists(path))
                {
                    return path;
                }
            }
        }

        if (Parameters.GenerateTestImageWhenMissing)
        {
            return EnsureSyntheticTestImage();
        }

        return Parameters.ImagePath ?? "";
    }

    /// <summary>Creates a simple dark frame with a bright blob for findContours / findCircles demos.</summary>
    private string EnsureSyntheticTestImage()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mdkoss-vision");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"capture-{Id}.png");
        using var mat = new Mat(480, 640, MatType.CV_8UC3, new Scalar(20, 20, 20));
        Cv2.Circle(mat, new Point(320, 240), 48, new Scalar(240, 240, 240), -1);
        Cv2.Circle(mat, new Point(320, 240), 12, new Scalar(40, 40, 40), -1);
        Cv2.ImWrite(path, mat);
        Vars.Set(BuildVarKey("lastGeneratedImage"), path);
        return path;
    }

    private void PublishStatusVars()
    {
        lock (_sync)
        {
            PublishStatusVarsUnlocked();
        }
    }

    private void PublishStatusVarsUnlocked()
    {
        Vars.Set(BuildVarKey("visionId"), Parameters.VisionId);
        Vars.Set(BuildVarKey("cameraDeviceId"), Parameters.CameraDeviceId);
        Vars.Set(BuildVarKey("imagePath"), Parameters.ImagePath);
        Vars.Set(BuildVarKey("resultPrefix"), Parameters.ResultPrefix);
        Vars.Set(BuildVarKey("runCount"), _runCount);
        Vars.Set(BuildVarKey("lastImagePath"), _lastImagePath ?? "");
        WriteState(State.ToString().ToLowerInvariant());
    }

    private void PublishResultVarsUnlocked(VisionRunResult result)
    {
        var prefix = string.IsNullOrWhiteSpace(Parameters.ResultPrefix) ? "vision" : Parameters.ResultPrefix.Trim();
        Vars.Set(BuildVarKey("lastOk"), result.Ok);
        Vars.Set(BuildVarKey("lastError"), result.Error ?? "");
        Vars.Set(BuildVarKey("lastScore"), result.Pose.Score);
        Vars.Set(BuildVarKey("lastX"), result.Pose.X);
        Vars.Set(BuildVarKey("lastY"), result.Pose.Y);
        Vars.Set(BuildVarKey("lastAngle"), result.Pose.AngleDeg);
        Vars.Set(BuildVarKey("lastDebugImage"), result.DebugImagePath ?? "");

        // Flat result keys for recipes / flow (prefix.x …).
        Vars.Set($"{prefix}.ok", result.Ok && result.Pose.Ok);
        Vars.Set($"{prefix}.x", result.Pose.X);
        Vars.Set($"{prefix}.y", result.Pose.Y);
        Vars.Set($"{prefix}.angle", result.Pose.AngleDeg);
        Vars.Set($"{prefix}.score", result.Pose.Score);
        Vars.Set($"{prefix}.error", result.Error ?? "");
        Vars.Set($"{prefix}.message", result.Pose.Message ?? "");

        foreach (var kv in result.Vars)
        {
            Vars.Set($"{prefix}.{kv.Key}", kv.Value);
        }
    }
}

/// <summary>Action handlers for <see cref="VisionDevice"/>.</summary>
public static class VisionDeviceActions
{
    public static DeviceActionResult Execute(
        VisionDevice device,
        string action,
        Dictionary<string, JsonElement>? parameters)
    {
        return action.Trim().ToLowerInvariant() switch
        {
            "run" => Run(device, parameters, capture: false),
            "capture" or "captureandrun" or "trigger" => Run(device, parameters, capture: true),
            "status" or "result" => Status(device),
            _ => DeviceActionResult.Fail("unknown_action"),
        };
    }

    private static DeviceActionResult Run(
        VisionDevice device,
        Dictionary<string, JsonElement>? parameters,
        bool capture)
    {
        var imagePath = ReadString(parameters, "imagePath", "path", "image");
        var visionId = ReadString(parameters, "visionId", "vision");
        var result = capture
            ? device.CaptureAndRun(
                string.IsNullOrWhiteSpace(imagePath) ? null : imagePath,
                string.IsNullOrWhiteSpace(visionId) ? null : visionId)
            : device.Run(
                string.IsNullOrWhiteSpace(imagePath) ? null : imagePath,
                string.IsNullOrWhiteSpace(visionId) ? null : visionId);

        return result.Ok
            ? DeviceActionResult.Ok(new
            {
                result.Ok,
                result.Error,
                pose = result.Pose,
                vars = result.Vars,
                result.DebugImagePath,
                log = result.Log,
            })
            : DeviceActionResult.Fail(result.Error ?? "vision_failed");
    }

    private static DeviceActionResult Status(VisionDevice device)
    {
        var last = device.LastResult;
        return DeviceActionResult.Ok(new
        {
            device.Id,
            visionId = device.Parameters.VisionId,
            cameraDeviceId = device.Parameters.CameraDeviceId,
            imagePath = device.Parameters.ImagePath,
            runCount = device.RunCount,
            lastOk = last?.Ok,
            lastError = last?.Error,
            pose = last?.Pose,
        });
    }

    private static string ReadString(Dictionary<string, JsonElement>? parameters, params string[] keys)
    {
        if (parameters is null)
        {
            return "";
        }

        foreach (var key in keys)
        {
            if (parameters.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString()?.Trim() ?? "";
            }
        }

        return "";
    }
}

/// <summary>Logical stub driver — vision I/O lives on the device.</summary>
internal sealed class VisionLogicalDriver : IDriver
{
    public string Name => "VISION";
    public bool IsConnected => true;
    public void Initialize(MdkSetting.DriverConfig config) { }
    public bool TryRead(string address, out object? value) { value = null; return false; }
    public bool Write(string address, object? value) => false;
    public bool TryReadDi(short diType, out int value) { value = 0; return false; }
    public bool TryReadDo(short doType, out int value) { value = 0; return false; }
    public bool WriteDo(short doType, int value) => false;
    public bool WriteDoBit(short doType, short doIndex, bool value) => false;
    public bool EnableAxis(short axis) => false;
    public bool DisableAxis(short axis) => false;
    public bool IsAxisEnabled(short axis) => false;
    public bool TryGetAxisStatus(short axis, out int status) { status = 0; return false; }
    public bool TryGetAxisPrfPosition(short axis, out double position) { position = 0; return false; }
    public bool TryGetAxisEncPosition(short axis, out double position) { position = 0; return false; }
    public bool TryGetAxisVelocity(short axis, out double velocity) { velocity = 0; return false; }
    public bool SetAxisPosition(short axis, double position) => false;
    public bool SetAxisVelocity(short axis, double velocity) => false;
    public bool SetAxisAcceleration(short axis, double acceleration) => false;
    public bool SetAxisDeceleration(short axis, double deceleration) => false;
    public bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration) => false;
    public bool MoveAxisJog(short axis, double velocity, double acceleration, double deceleration) => false;
    public bool MoveAxisHome(short axis, short homeMode, double velocity, double acceleration, double deceleration) => false;
    public bool Stop(int axisMask, int option = 0) => false;
    public void Dispose() { }
}
