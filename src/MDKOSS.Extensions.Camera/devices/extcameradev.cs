using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Extensions.Camera;

/// <summary>Last simulated capture / vision result from <see cref="ExtCameraDevice"/>.</summary>
public sealed record ExtCameraCaptureResult(
    string CaptureId,
    string Recipe,
    long TimestampUnixMs,
    int Width,
    int Height,
    double OffsetX,
    double OffsetY,
    double AngleDeg,
    bool Ok);

/// <summary>
/// Extension camera device (config type <c>extcamera</c>).
/// Demo backend is software simulation — replace Open/TriggerCapture with a real SDK later.
/// Distinct from Core's built-in placeholder <c>cameradev</c>.
/// </summary>
public sealed class ExtCameraDevice : MDeviceBase
{
    private readonly object _sync = new();
    private readonly Random _random = new();
    private bool _isOpen;
    private ExtCameraCaptureResult? _lastResult;
    private int _captureCount;

    public ExtCameraDevice(string id, string name, ExtCameraDeviceParameters parameters, MVarStore vars)
        : base(id, name, MDeviceType.Generic, new ExtCameraLogicalDriver(), vars)
    {
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        PublishStatusVars();
    }

    public ExtCameraDeviceParameters Parameters { get; }

    public bool IsOpen
    {
        get { lock (_sync) return _isOpen; }
    }

    public ExtCameraCaptureResult? LastResult
    {
        get { lock (_sync) return _lastResult; }
    }

    public int CaptureCount
    {
        get { lock (_sync) return _captureCount; }
    }

    /// <summary>Opens the camera session (sim: marks ready).</summary>
    public bool Open()
    {
        lock (_sync)
        {
            _isOpen = true;
            State = MDeviceState.Running;
            PublishStatusVarsUnlocked();
            return true;
        }
    }

    /// <summary>Closes the camera session.</summary>
    public bool Close()
    {
        lock (_sync)
        {
            _isOpen = false;
            State = MDeviceState.Stopped;
            PublishStatusVarsUnlocked();
            return true;
        }
    }

    /// <summary>
    /// Triggers a capture. Simulation returns noisy XY / angle offsets for recipe tooling.
    /// </summary>
    public ExtCameraCaptureResult? TriggerCapture(string recipe)
    {
        lock (_sync)
        {
            if (!_isOpen)
            {
                return null;
            }

            recipe = string.IsNullOrWhiteSpace(recipe) ? "default" : recipe.Trim();
            var noise = Parameters.NoisePx;
            var result = new ExtCameraCaptureResult(
                CaptureId: Guid.NewGuid().ToString("N"),
                Recipe: recipe,
                TimestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Width: Parameters.Width,
                Height: Parameters.Height,
                OffsetX: NextNoise(noise),
                OffsetY: NextNoise(noise),
                AngleDeg: NextNoise(noise * 0.1),
                Ok: true);

            _lastResult = result;
            _captureCount++;
            PublishResultVarsUnlocked(result);
            PublishStatusVarsUnlocked();
            return result;
        }
    }

    public override void Start()
    {
        // Do not call EnsureConnected — session is managed by Open/Close.
        State = MDeviceState.Initialized;
        WriteState("initialized");
        PublishStatusVars();
    }

    public override void Stop()
    {
        Close();
        base.Stop();
    }

    public override void Dispose()
    {
        Close();
        base.Dispose();
    }

    public override DeviceSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return new DeviceSnapshot(
                Id,
                Name,
                "extcamera",
                State.ToString(),
                $"camera:{Parameters.Backend}",
                _isOpen);
        }
    }

    private double NextNoise(double amplitude)
    {
        if (amplitude <= 0)
        {
            return 0;
        }

        return (_random.NextDouble() * 2 - 1) * amplitude;
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
        Vars.Set(BuildVarKey("isOpen"), _isOpen);
        Vars.Set(BuildVarKey("backend"), Parameters.Backend);
        Vars.Set(BuildVarKey("deviceIndex"), Parameters.DeviceIndex);
        Vars.Set(BuildVarKey("width"), Parameters.Width);
        Vars.Set(BuildVarKey("height"), Parameters.Height);
        Vars.Set(BuildVarKey("exposureMs"), Parameters.ExposureMs);
        Vars.Set(BuildVarKey("captureCount"), _captureCount);
        WriteState(State.ToString().ToLowerInvariant());
    }

    private void PublishResultVarsUnlocked(ExtCameraCaptureResult result)
    {
        Vars.Set(BuildVarKey("lastCaptureId"), result.CaptureId);
        Vars.Set(BuildVarKey("lastCaptureRecipe"), result.Recipe);
        Vars.Set(BuildVarKey("lastOffsetX"), result.OffsetX);
        Vars.Set(BuildVarKey("lastOffsetY"), result.OffsetY);
        Vars.Set(BuildVarKey("lastAngleDeg"), result.AngleDeg);
        Vars.Set(BuildVarKey("lastOk"), result.Ok);
    }
}

/// <summary>Minimal IDriver stub — real camera I/O lives on the device, not a motion card.</summary>
internal sealed class ExtCameraLogicalDriver : IDriver
{
    public string Name => "EXTCAMERA";

    public bool IsConnected => true;

    public void Initialize(MdkSetting.DriverConfig config) { }

    public bool TryRead(string address, out object? value)
    {
        value = null;
        return false;
    }

    public bool Write(string address, object? value) => false;

    public bool TryReadDi(short diType, out int value)
    {
        value = 0;
        return false;
    }

    public bool TryReadDo(short doType, out int value)
    {
        value = 0;
        return false;
    }

    public bool WriteDo(short doType, int value) => false;

    public bool WriteDoBit(short doType, short doIndex, bool value) => false;

    public bool EnableAxis(short axis) => false;

    public bool DisableAxis(short axis) => false;

    public bool IsAxisEnabled(short axis) => false;

    public bool TryGetAxisStatus(short axis, out int status)
    {
        status = 0;
        return false;
    }

    public bool TryGetAxisPrfPosition(short axis, out double position)
    {
        position = 0;
        return false;
    }

    public bool TryGetAxisEncPosition(short axis, out double position)
    {
        position = 0;
        return false;
    }

    public bool TryGetAxisVelocity(short axis, out double velocity)
    {
        velocity = 0;
        return false;
    }

    public bool SetAxisPosition(short axis, double position) => false;

    public bool SetAxisVelocity(short axis, double velocity) => false;

    public bool SetAxisAcceleration(short axis, double acceleration) => false;

    public bool SetAxisDeceleration(short axis, double deceleration) => false;

    public bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration)
        => false;

    public bool MoveAxisJog(short axis, double velocity, double acceleration, double deceleration) => false;

    public bool MoveAxisHome(short axis, short homeMode, double velocity, double acceleration, double deceleration)
        => false;

    public bool Stop(int axisMask, int option = 0) => false;

    public void Dispose() { }
}
