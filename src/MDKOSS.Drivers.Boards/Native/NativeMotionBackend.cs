using System.Globalization;
using MDKOSS.Core;

namespace MDKOSS.Drivers.Boards;

/// <summary>Minimal live-card surface. Only functions used by <see cref="NativeCardDriver"/> are declared per vendor.</summary>
internal abstract class NativeMotionBackend : IDisposable
{
    protected readonly object Gate = new();

    public abstract string Vendor { get; }

    public abstract bool TryOpen(MdkSetting.DriverConfig config, BoardKind kind, out string error);

    public abstract void Close();

    public virtual bool TryReadDiBit(int bit, out bool on)
    {
        on = false;
        return false;
    }

    public virtual bool TryReadDoBit(int bit, out bool on)
    {
        on = false;
        return false;
    }

    public virtual bool WriteDoBit(int bit, bool on) => false;

    public virtual bool TryReadDiPort(out int word)
    {
        word = 0;
        var acc = 0;
        for (var i = 0; i < 32; i++)
        {
            if (!TryReadDiBit(i, out var on))
            {
                continue;
            }

            if (on)
            {
                acc |= 1 << i;
            }
        }

        word = acc;
        return true;
    }

    public virtual bool TryReadDoPort(out int word)
    {
        word = 0;
        var acc = 0;
        for (var i = 0; i < 32; i++)
        {
            if (!TryReadDoBit(i, out var on))
            {
                continue;
            }

            if (on)
            {
                acc |= 1 << i;
            }
        }

        word = acc;
        return true;
    }

    public virtual bool WriteDoPort(int word)
    {
        var ok = true;
        for (var i = 0; i < 32; i++)
        {
            ok &= WriteDoBit(i, (word & (1 << i)) != 0);
        }

        return ok;
    }

    public virtual bool EnableAxis(short axis, bool on) => false;

    public virtual bool IsAxisEnabled(short axis) => false;

    public virtual bool TryGetPrfPos(short axis, out double pos)
    {
        pos = 0;
        return false;
    }

    public virtual bool TryGetEncPos(short axis, out double pos) => TryGetPrfPos(axis, out pos);

    public virtual bool TryGetStatus(short axis, out int status)
    {
        status = 0;
        return false;
    }

    public virtual bool TryGetVel(short axis, out double vel)
    {
        vel = 0;
        return false;
    }

    public virtual bool SetPosition(short axis, double pos) => false;

    public virtual bool SetVelocity(short axis, double vel) => false;

    public virtual bool SetAcc(short axis, double acc) => false;

    public virtual bool SetDec(short axis, double dec) => false;

    public virtual bool MoveTrap(short axis, int target, double vel, double acc, double dec) => false;

    public virtual bool MoveJog(short axis, double vel, double acc, double dec) => false;

    public virtual bool MoveHome(short axis, short mode, double vel, double acc, double dec) => false;

    public virtual bool Stop(int axisMask, int option) => false;

    public void Dispose() => Close();

    public static NativeMotionBackend Create(BoardKind kind) => kind.Type.ToLowerInvariant() switch
    {
        "zmc" or "zmotion" => new NativeZmc(),
        "adt" => new NativeAdt(),
        "mpc" => new NativeMpc(),
        "emc" => new NativeEmc(),
        "gtn" => new NativeGtn(),
        "adlink" => new NativeAdlink(),
        "advantech" => new NativeAdvantech(),
        "galil" => new NativeGalil(),
        "inovance" => new NativeInovance(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind.Type, "Unknown board type."),
    };

    protected static string GetString(MdkSetting.DriverConfig config, string key, string fallback)
    {
        if (config.Parameters is null || !config.Parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return raw.Trim();
    }

    protected static int GetInt(MdkSetting.DriverConfig config, string key, int fallback)
    {
        if (config.Parameters is null || !config.Parameters.TryGetValue(key, out var raw))
        {
            return fallback;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
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
}
