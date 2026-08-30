using System.Runtime.InteropServices;
using MDKOSS.Core;

namespace MDKOSS.Drivers.Boards;

/// <summary>汇川 IMC30G / IMC60G（IMC_API_x64.dll）。公开笔记：IMC_Open(handle, netId, imcId)。</summary>
internal sealed class NativeInovance : NativeMotionBackend
{
    private IntPtr _handle;
    private readonly HashSet<short> _enabled = [];

    public override string Vendor => "汇川 Inovance";

    public override bool TryOpen(MdkSetting.DriverConfig config, BoardKind kind, out string error)
    {
        var net = GetInt(config, "netId", 0);
        var imc = GetInt(config, "card", 0);
        return CatchNative(() => Native.IMC_Open(out _handle, net, imc) == 0 && _handle != IntPtr.Zero, out error);
    }

    public override void Close()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _ = Native.IMC_Close(_handle);
        }
        catch (Exception)
        {
        }

        _handle = IntPtr.Zero;
        _enabled.Clear();
    }

    public override bool TryReadDiBit(int bit, out bool on)
    {
        on = false;
        if (Native.IMC_GetDiBit(_handle, bit, out var v) != 0)
        {
            return false;
        }

        on = v != 0;
        return true;
    }

    public override bool WriteDoBit(int bit, bool on) => Native.IMC_SetDoBit(_handle, bit, on ? 1 : 0) == 0;

    public override bool EnableAxis(short axis, bool on)
    {
        if (Native.IMC_SetAxSvOn(_handle, axis, on ? 1 : 0) != 0)
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
        return Native.IMC_GetAxPrfPos(_handle, axis, out pos) == 0;
    }

    public override bool TryGetEncPos(short axis, out double pos)
    {
        pos = 0;
        return Native.IMC_GetAxEncPos(_handle, axis, out pos) == 0;
    }

    public override bool SetVelocity(short axis, double vel) => Native.IMC_SetAxVel(_handle, axis, vel) == 0;

    public override bool SetAcc(short axis, double acc) => Native.IMC_SetAxAcc(_handle, axis, acc) == 0;

    public override bool SetDec(short axis, double dec) => Native.IMC_SetAxDec(_handle, axis, dec) == 0;

    public override bool MoveTrap(short axis, int target, double vel, double acc, double dec)
        => SetVelocity(axis, vel)
           && SetAcc(axis, acc)
           && SetDec(axis, dec)
           && Native.IMC_SetAxPtpAbs(_handle, axis, target) == 0;

    public override bool MoveJog(short axis, double vel, double acc, double dec)
        => SetVelocity(axis, Math.Abs(vel))
           && Native.IMC_SetAxJog(_handle, axis, vel >= 0 ? 1 : 0) == 0;

    public override bool MoveHome(short axis, short mode, double vel, double acc, double dec)
        => Native.IMC_SetAxHome(_handle, axis, mode) == 0;

    public override bool Stop(int axisMask, int option)
    {
        var ok = true;
        for (short a = 0; a < 16; a++)
        {
            if (axisMask != 0 && ((axisMask >> a) & 1) == 0)
            {
                continue;
            }

            ok &= Native.IMC_SetAxStop(_handle, a, option) == 0;
        }

        return ok;
    }

    private static class Native
    {
        private const string Dll = "IMC_API_x64.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMC_Open(out IntPtr handle, int netId, int imcId);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMC_Close(IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMC_SetAxSvOn(IntPtr handle, int axis, int on);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMC_GetAxPrfPos(IntPtr handle, int axis, out double pos);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMC_GetAxEncPos(IntPtr handle, int axis, out double pos);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMC_SetAxPtpAbs(IntPtr handle, int axis, double pos);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMC_SetAxJog(IntPtr handle, int axis, int dir);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMC_SetAxStop(IntPtr handle, int axis, int mode);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMC_SetAxHome(IntPtr handle, int axis, int mode);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMC_SetAxVel(IntPtr handle, int axis, double vel);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMC_SetAxAcc(IntPtr handle, int axis, double acc);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMC_SetAxDec(IntPtr handle, int axis, double dec);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMC_GetDiBit(IntPtr handle, int bit, out int value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int IMC_SetDoBit(IntPtr handle, int bit, int value);
    }
}
