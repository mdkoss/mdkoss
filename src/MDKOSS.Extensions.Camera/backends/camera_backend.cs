namespace MDKOSS.Extensions.Camera;

/// <summary>Acquisition mode requested by config / runtime actions.</summary>
public enum CameraTriggerMode
{
    /// <summary>Free-run; every <c>trigger</c> just pulls the newest frame.</summary>
    Continuous,

    /// <summary>SDK issues a software trigger before each grab.</summary>
    Software,

    /// <summary>External line trigger; <c>trigger</c> waits for the hardware pulse.</summary>
    Hardware,
}

/// <summary>One grabbed frame, still in the camera's native pixel layout (GenICam PFNC code).</summary>
public sealed record CameraFrame(
    int Width,
    int Height,
    uint PixelFormat,
    byte[] Data,
    long FrameId,
    long TimestampUnixMs);

/// <summary>One camera reported by <see cref="CameraBackend.Enumerate"/>.</summary>
public sealed record CameraDeviceInfo(
    int Index,
    string Model,
    string Serial,
    string Vendor,
    string Transport);

/// <summary>
/// Minimal live-camera surface. Only the calls <see cref="ExtCameraDevice"/> needs are declared per vendor:
/// open by index/serial, exposure / gain / trigger, and a single blocking grab.
/// </summary>
internal abstract class CameraBackend : IDisposable
{
    protected readonly object Gate = new();

    public abstract string Vendor { get; }

    /// <summary>Opens the vendor session. Returns false with a reason instead of throwing.</summary>
    public abstract bool TryOpen(ExtCameraDeviceParameters parameters, out string error);

    public abstract void Close();

    /// <summary>Cameras visible to this SDK; empty when the SDK cannot enumerate without opening.</summary>
    public virtual IReadOnlyList<CameraDeviceInfo> Enumerate() => [];

    public virtual bool TrySetExposure(double microseconds) => false;

    public virtual bool TrySetGain(double gain) => false;

    public virtual bool TrySetTrigger(CameraTriggerMode mode) => false;

    /// <summary>Starts streaming. Called lazily before the first grab.</summary>
    public virtual bool StartGrab() => true;

    public virtual void StopGrab() { }

    public abstract bool TryGrab(int timeoutMs, out CameraFrame? frame, out string error);

    public void Dispose() => Close();

    public static CameraBackend Create(CameraKind kind) => kind.Type.ToLowerInvariant() switch
    {
        "sim" => new SimCameraBackend(),
        "file" => new FileCameraBackend(),
        "uvc" => new OpenCvCameraBackend(),
        "hik" => new NativeHikCamera(),
        "daheng" => new NativeDahengCamera(),
        "huaray" => new NativeHuarayCamera(),
        "mindvision" => new NativeMindVisionCamera(),
        "basler" => new NativeBaslerCamera(),
        "flir" => new NativeSpinnakerCamera(),
        "tis" => new NativeTisCamera(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind.Type, "Unknown camera backend."),
    };

    /// <summary>Binds the configured <c>nativeDll</c> override for this kind before the first P/Invoke.</summary>
    protected static void BindNativeDll(ExtCameraDeviceParameters parameters)
    {
        var kind = parameters.Kind;
        if (string.IsNullOrWhiteSpace(kind.NativeDll))
        {
            return;
        }

        NativeDllMap.Bind(
            kind.NativeDll,
            string.IsNullOrWhiteSpace(parameters.NativeDll) ? kind.NativeDll : parameters.NativeDll);
    }

    /// <summary>Runs a vendor open sequence, turning any SDK/DLL failure into a reason string.</summary>
    protected static bool TryOpenNative(Func<bool> open, out string error)
    {
        if (CatchNative(open, out error))
        {
            return true;
        }

        if (string.IsNullOrEmpty(error))
        {
            error = "open_failed";
        }

        return false;
    }

    /// <summary>Runs a vendor grab sequence; a null result is reported as <c>grab_failed</c>.</summary>
    protected static bool TryGrabNative(Func<CameraFrame?> grab, out CameraFrame? frame, out string error)
    {
        CameraFrame? grabbed = null;
        var ok = CatchNative(
            () =>
            {
                grabbed = grab();
                return grabbed is not null;
            },
            out error);

        frame = grabbed;
        if (ok)
        {
            return true;
        }

        if (string.IsNullOrEmpty(error))
        {
            error = "grab_failed";
        }

        return false;
    }

    protected static bool CatchNative(Func<bool> call, out string error)
    {
        error = "";
        try
        {
            return call();
        }
        catch (DllNotFoundException ex)
        {
            error = "native_dll_missing:" + ex.Message;
            return false;
        }
        catch (BadImageFormatException ex)
        {
            error = "native_bad_image:" + ex.Message;
            return false;
        }
        catch (EntryPointNotFoundException ex)
        {
            error = "native_entry_missing:" + ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    protected static long NowUnixMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
