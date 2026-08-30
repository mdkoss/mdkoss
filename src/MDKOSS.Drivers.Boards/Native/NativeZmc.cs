using System.Runtime.InteropServices;
using MDKOSS.Core;

namespace MDKOSS.Drivers.Boards;

/// <summary>正运动 ZAux（zauxdll.dll）。手册：ZAux_OpenEth / Direct_Single_MoveAbs。</summary>
internal sealed class NativeZmc : NativeMotionBackend
{
    private IntPtr _handle;
    private readonly HashSet<short> _enabled = [];

    public override string Vendor => "正运动 Zmotion";

    public override bool TryOpen(MdkSetting.DriverConfig config, BoardKind kind, out string error)
    {
        var ip = GetString(config, "ip", "192.168.0.11");
        var card = GetInt(config, "card", 0);
        return CatchNative(() =>
        {
            var rc = string.IsNullOrWhiteSpace(ip) || ip is "0" or "pci"
                ? Native.ZAux_OpenPci(card, out _handle)
                : Native.ZAux_OpenEth(ip, out _handle);
            return rc == 0 && _handle != IntPtr.Zero;
        }, out error);
    }

    public override void Close()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _ = Native.ZAux_Close(_handle);
        }
        catch (Exception)
        {
            // DLL already gone
        }

        _handle = IntPtr.Zero;
        _enabled.Clear();
    }

    public override bool TryReadDiBit(int bit, out bool on)
    {
        on = false;
        lock (Gate)
        {
            if (Native.ZAux_Direct_GetIn(_handle, bit, out var v) != 0)
            {
                return false;
            }

            on = v != 0;
            return true;
        }
    }

    public override bool TryReadDoBit(int bit, out bool on)
    {
        on = false;
        lock (Gate)
        {
            if (Native.ZAux_Direct_GetOp(_handle, bit, out var v) != 0)
            {
                return false;
            }

            on = v != 0;
            return true;
        }
    }

    public override bool WriteDoBit(int bit, bool on)
        => Ok(Native.ZAux_Direct_SetOp(_handle, bit, on ? 1u : 0u));

    public override bool EnableAxis(short axis, bool on)
    {
        if (!Ok(Native.ZAux_Direct_SetAxisEnable(_handle, axis, on ? 1 : 0)))
        {
            return false;
        }

        if (on)
        {
            _enabled.Add(axis);
        }
        else
        {
            _enabled.Remove(axis);
        }

        return true;
    }

    public override bool IsAxisEnabled(short axis) => _enabled.Contains(axis);

    public override bool TryGetPrfPos(short axis, out double pos)
    {
        pos = 0;
        lock (Gate)
        {
            if (Native.ZAux_Direct_GetDpos(_handle, axis, out var v) != 0)
            {
                return false;
            }

            pos = v;
            return true;
        }
    }

    public override bool TryGetEncPos(short axis, out double pos)
    {
        pos = 0;
        lock (Gate)
        {
            if (Native.ZAux_Direct_GetMpos(_handle, axis, out var v) != 0)
            {
                return false;
            }

            pos = v;
            return true;
        }
    }

    public override bool TryGetStatus(short axis, out int status)
    {
        status = 0;
        lock (Gate)
        {
            if (Native.ZAux_Direct_GetIfIdle(_handle, axis, out var idle) != 0)
            {
                return false;
            }

            if (_enabled.Contains(axis))
            {
                status |= 1 << 1;
            }

            if (idle == 0)
            {
                status |= 1 << 10;
            }

            return true;
        }
    }

    public override bool SetPosition(short axis, double pos)
        => Ok(Native.ZAux_Direct_SetDpos(_handle, axis, (float)pos));

    public override bool SetVelocity(short axis, double vel)
        => Ok(Native.ZAux_Direct_SetSpeed(_handle, axis, (float)vel));

    public override bool SetAcc(short axis, double acc)
        => Ok(Native.ZAux_Direct_SetAccel(_handle, axis, (float)acc));

    public override bool SetDec(short axis, double dec)
        => Ok(Native.ZAux_Direct_SetDecel(_handle, axis, (float)dec));

    public override bool MoveTrap(short axis, int target, double vel, double acc, double dec)
        => SetVelocity(axis, vel)
           && SetAcc(axis, acc)
           && SetDec(axis, dec)
           && Ok(Native.ZAux_Direct_Single_MoveAbs(_handle, axis, target));

    public override bool MoveJog(short axis, double vel, double acc, double dec)
    {
        var dir = vel >= 0 ? 1 : 0;
        return SetVelocity(axis, Math.Abs(vel))
               && SetAcc(axis, acc)
               && SetDec(axis, dec)
               && Ok(Native.ZAux_Direct_Single_Vmove(_handle, axis, dir));
    }

    public override bool MoveHome(short axis, short mode, double vel, double acc, double dec)
        => SetVelocity(axis, vel)
           && SetAcc(axis, acc)
           && SetDec(axis, dec)
           && Ok(Native.ZAux_Direct_Single_Datum(_handle, axis, mode));

    public override bool Stop(int axisMask, int option)
    {
        var ok = true;
        for (short a = 0; a < 16; a++)
        {
            if (axisMask != 0 && ((axisMask >> a) & 1) == 0)
            {
                continue;
            }

            ok &= Ok(Native.ZAux_Direct_Single_Cancel(_handle, a, option == 0 ? 2 : option));
        }

        return ok;
    }

    private bool Ok(int rc)
    {
        lock (Gate)
        {
            return rc == 0;
        }
    }

    private static class Native
    {
        private const string Dll = "zauxdll.dll";

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_OpenEth(string ipaddr, out IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_OpenPci(int cardnum, out IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_Close(IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_Direct_GetIn(IntPtr handle, int ionum, out uint value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_Direct_GetOp(IntPtr handle, int ionum, out uint value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_Direct_SetOp(IntPtr handle, int ionum, uint value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_Direct_SetAxisEnable(IntPtr handle, int iaxis, int value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_Direct_SetSpeed(IntPtr handle, int iaxis, float value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_Direct_SetAccel(IntPtr handle, int iaxis, float value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_Direct_SetDecel(IntPtr handle, int iaxis, float value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_Direct_Single_MoveAbs(IntPtr handle, int iaxis, float pos);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_Direct_Single_Vmove(IntPtr handle, int iaxis, int dir);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_Direct_Single_Cancel(IntPtr handle, int iaxis, int mode);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_Direct_Single_Datum(IntPtr handle, int iaxis, int mode);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_Direct_GetDpos(IntPtr handle, int iaxis, out float value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_Direct_GetMpos(IntPtr handle, int iaxis, out float value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_Direct_SetDpos(IntPtr handle, int iaxis, float value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int ZAux_Direct_GetIfIdle(IntPtr handle, int iaxis, out int value);
    }
}
