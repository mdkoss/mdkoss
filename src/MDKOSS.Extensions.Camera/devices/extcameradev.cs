using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Extensions.Camera;

/// <summary>Last capture performed by <see cref="ExtCameraDevice"/>.</summary>
public sealed record ExtCameraCaptureResult(
    string CaptureId,
    string Recipe,
    long TimestampUnixMs,
    int Width,
    int Height,
    string PixelFormat,
    int Bytes,
    double OffsetX,
    double OffsetY,
    double AngleDeg,
    string ImagePath,
    string Backend,
    bool Ok);

/// <summary>
/// Extension camera device (config type <c>extcamera</c>). The <c>backend</c> parameter selects a
/// <see cref="CameraCatalog"/> entry — simulator, image folder, UVC, or a vendor SDK (海康 / 大恒 /
/// 华睿 / 迈德威视 / Basler / FLIR / 映美精). A live backend that cannot open degrades to the
/// simulator when <c>fallbackToSim</c> is on, so a missing SDK never faults the runtime.
/// Distinct from Core's built-in placeholder <c>cameradev</c>.
/// </summary>
public sealed class ExtCameraDevice : MDeviceBase
{
    private readonly object _sync = new();
    private readonly Random _random = new();
    private CameraBackend? _backend;
    private CameraKind _effectiveKind;
    private bool _isOpen;
    private bool _grabbing;
    private string _lastError = "";
    private ExtCameraCaptureResult? _lastResult;
    private CameraFrame? _lastFrame;
    private int _captureCount;
    private int _failCount;

    public ExtCameraDevice(string id, string name, ExtCameraDeviceParameters parameters, MVarStore vars)
        : base(id, name, MDeviceType.Generic, new ExtCameraLogicalDriver(), vars)
    {
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _effectiveKind = Parameters.Kind;
        PublishStatusVars();
    }

    public ExtCameraDeviceParameters Parameters { get; }

    /// <summary>Backend actually in use — differs from the configured one after a fallback.</summary>
    public CameraKind EffectiveKind
    {
        get { lock (_sync) return _effectiveKind; }
    }

    public bool IsOpen
    {
        get { lock (_sync) return _isOpen; }
    }

    public bool IsGrabbing
    {
        get { lock (_sync) return _grabbing; }
    }

    public string LastError
    {
        get { lock (_sync) return _lastError; }
    }

    public ExtCameraCaptureResult? LastResult
    {
        get { lock (_sync) return _lastResult; }
    }

    public int CaptureCount
    {
        get { lock (_sync) return _captureCount; }
    }

    public int FailCount
    {
        get { lock (_sync) return _failCount; }
    }

    /// <summary>Opens the camera session, falling back to the simulator when configured.</summary>
    public bool Open()
    {
        lock (_sync)
        {
            if (_isOpen)
            {
                return true;
            }

            var configured = Parameters.Kind;
            _lastError = "";
            if (TryOpenKind(configured, Parameters))
            {
                return FinishOpenUnlocked(configured);
            }

            if (!Parameters.FallbackToSim || configured.Type == CameraCatalog.Sim.Type)
            {
                State = MDeviceState.Fault;
                PublishStatusVarsUnlocked();
                return false;
            }

            if (!TryOpenKind(CameraCatalog.Sim, Parameters.WithBackend(CameraCatalog.Sim.Type)))
            {
                State = MDeviceState.Fault;
                PublishStatusVarsUnlocked();
                return false;
            }

            return FinishOpenUnlocked(CameraCatalog.Sim);
        }
    }

    public bool Close()
    {
        lock (_sync)
        {
            _backend?.StopGrab();
            _backend?.Dispose();
            _backend = null;
            _isOpen = false;
            _grabbing = false;
            State = MDeviceState.Stopped;
            PublishStatusVarsUnlocked();
            return true;
        }
    }

    /// <summary>Cameras visible to the configured backend's SDK (does not require an open session).</summary>
    public IReadOnlyList<CameraDeviceInfo> Enumerate()
    {
        lock (_sync)
        {
            if (_backend is not null)
            {
                return _backend.Enumerate();
            }
        }

        try
        {
            using var probe = CameraBackend.Create(Parameters.Kind);
            return probe.Enumerate();
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                _lastError = ex.Message;
            }

            return [];
        }
    }

    public bool StartGrab()
    {
        lock (_sync)
        {
            if (_backend is null)
            {
                return false;
            }

            _grabbing = _backend.StartGrab();
            PublishStatusVarsUnlocked();
            return _grabbing;
        }
    }

    public void StopGrab()
    {
        lock (_sync)
        {
            _backend?.StopGrab();
            _grabbing = false;
            PublishStatusVarsUnlocked();
        }
    }

    public bool SetExposure(double microseconds)
    {
        lock (_sync)
        {
            return _backend is not null && _backend.TrySetExposure(microseconds);
        }
    }

    public bool SetGain(double gain)
    {
        lock (_sync)
        {
            return _backend is not null && _backend.TrySetGain(gain);
        }
    }

    public bool SetTrigger(CameraTriggerMode mode)
    {
        lock (_sync)
        {
            return _backend is not null && _backend.TrySetTrigger(mode);
        }
    }

    /// <summary>Triggers one acquisition. Returns <c>null</c> when the camera is not open.</summary>
    public ExtCameraCaptureResult? TriggerCapture(string recipe)
    {
        lock (_sync)
        {
            if (_backend is null || !_isOpen)
            {
                return null;
            }

            recipe = string.IsNullOrWhiteSpace(recipe) ? "default" : recipe.Trim();
            if (!_grabbing)
            {
                _grabbing = _backend.StartGrab();
            }

            if (!_backend.TryGrab(Parameters.TimeoutMs, out var frame, out var error) || frame is null)
            {
                _failCount++;
                _lastError = string.IsNullOrWhiteSpace(error) ? "grab_failed" : error;
                State = MDeviceState.Fault;
                PublishStatusVarsUnlocked();
                return null;
            }

            _lastFrame = frame;
            _lastError = "";
            var imagePath = SaveFrameUnlocked(frame);
            var isSim = _effectiveKind.Type == CameraCatalog.Sim.Type;
            var noise = isSim ? Parameters.NoisePx : 0;
            var result = new ExtCameraCaptureResult(
                CaptureId: Guid.NewGuid().ToString("N"),
                Recipe: recipe,
                TimestampUnixMs: frame.TimestampUnixMs,
                Width: frame.Width,
                Height: frame.Height,
                PixelFormat: CameraPixel.Describe(frame.PixelFormat),
                Bytes: frame.Data.Length,
                OffsetX: NextNoise(noise),
                OffsetY: NextNoise(noise),
                AngleDeg: NextNoise(noise * 0.1),
                ImagePath: imagePath,
                Backend: _effectiveKind.Type,
                Ok: true);

            _lastResult = result;
            _captureCount++;
            State = MDeviceState.Running;
            PublishResultVarsUnlocked(result);
            PublishStatusVarsUnlocked();
            return result;
        }
    }

    /// <summary>Encodes the most recent frame for HTTP or disk. Returns an empty array when nothing was grabbed.</summary>
    public byte[] EncodeLastFrame(string extension)
    {
        lock (_sync)
        {
            if (_lastFrame is null)
            {
                return [];
            }

            try
            {
                return CameraPixel.Encode(_lastFrame, extension);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return [];
            }
        }
    }

    public override void Start()
    {
        // Do not call EnsureConnected — the session is managed by Open/Close.
        State = MDeviceState.Initialized;
        WriteState("initialized");
        PublishStatusVars();
        if (Parameters.AutoOpen)
        {
            Open();
        }
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
                $"camera:{_effectiveKind.Type}",
                _isOpen);
        }
    }

    private bool TryOpenKind(CameraKind kind, ExtCameraDeviceParameters parameters)
    {
        try
        {
            var backend = CameraBackend.Create(kind);
            if (backend.TryOpen(parameters, out var error))
            {
                _backend?.Dispose();
                _backend = backend;
                _effectiveKind = kind;
                return true;
            }

            backend.Dispose();
            _lastError = string.IsNullOrWhiteSpace(error) ? "open_failed" : error;
            return false;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return false;
        }
    }

    private bool FinishOpenUnlocked(CameraKind kind)
    {
        _effectiveKind = kind;
        _isOpen = true;
        _grabbing = _backend?.StartGrab() ?? false;
        State = MDeviceState.Running;
        PublishStatusVarsUnlocked();
        return true;
    }

    private string SaveFrameUnlocked(CameraFrame frame)
    {
        if (string.IsNullOrWhiteSpace(Parameters.SaveDir))
        {
            return "";
        }

        try
        {
            Directory.CreateDirectory(Parameters.SaveDir);
            var ext = CameraPixel.NormalizeExtension(Parameters.SaveFormat);
            var path = Path.Combine(Parameters.SaveDir, $"{Id}-{frame.TimestampUnixMs}{ext}");
            var bytes = CameraPixel.Encode(frame, ext);
            if (bytes.Length == 0)
            {
                return "";
            }

            File.WriteAllBytes(path, bytes);
            return path;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return "";
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
        Vars.Set(BuildVarKey("grabbing"), _grabbing);
        Vars.Set(BuildVarKey("backend"), Parameters.Backend);
        Vars.Set(BuildVarKey("effectiveBackend"), _effectiveKind.Type);
        Vars.Set(BuildVarKey("vendor"), _effectiveKind.Vendor);
        Vars.Set(BuildVarKey("deviceIndex"), Parameters.DeviceIndex);
        Vars.Set(BuildVarKey("serialNumber"), Parameters.SerialNumber);
        Vars.Set(BuildVarKey("width"), Parameters.Width);
        Vars.Set(BuildVarKey("height"), Parameters.Height);
        Vars.Set(BuildVarKey("exposureUs"), Parameters.ExposureUs);
        Vars.Set(BuildVarKey("exposureMs"), Parameters.ExposureMs);
        Vars.Set(BuildVarKey("gain"), Parameters.Gain);
        Vars.Set(BuildVarKey("triggerMode"), Parameters.TriggerMode.ToString().ToLowerInvariant());
        Vars.Set(BuildVarKey("captureCount"), _captureCount);
        Vars.Set(BuildVarKey("failCount"), _failCount);
        Vars.Set(BuildVarKey("lastError"), _lastError);
        WriteState(State.ToString().ToLowerInvariant());
    }

    private void PublishResultVarsUnlocked(ExtCameraCaptureResult result)
    {
        Vars.Set(BuildVarKey("lastCaptureId"), result.CaptureId);
        Vars.Set(BuildVarKey("lastCaptureRecipe"), result.Recipe);
        Vars.Set(BuildVarKey("lastOffsetX"), result.OffsetX);
        Vars.Set(BuildVarKey("lastOffsetY"), result.OffsetY);
        Vars.Set(BuildVarKey("lastAngleDeg"), result.AngleDeg);
        Vars.Set(BuildVarKey("lastPixelFormat"), result.PixelFormat);

        // VisionDevice reads this key to feed the pipeline with the freshly grabbed image.
        Vars.Set(BuildVarKey("lastImagePath"), result.ImagePath);
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
