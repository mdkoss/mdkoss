using System.Runtime.InteropServices;
using MDKOSS.Core;

namespace MDKOSS.Drivers.Boards;

/// <summary>研华 Common Motion（ADVMOT.dll）。Acm_DevOpen → Acm_AxOpen → Acm_AxMoveAbs。</summary>
internal sealed class NativeAdvantech : NativeMotionBackend
{
    private IntPtr _dev;
    private readonly Dictionary<short, IntPtr> _axes = [];

    public override string Vendor => "研华 Advantech";

    public override bool TryOpen(MdkSetting.DriverConfig config, BoardKind kind, out string error)
    {
        var device = (uint)GetInt(config, "card", 0);
        var cfg = GetString(config, "configPath", "");
        var axisCount = GetInt(config, "axisCount", 4);
        return CatchNative(() =>
        {
            if (Native.Acm_DevOpen(device, out _dev) != 0 || _dev == IntPtr.Zero)
            {
                return false;
            }

            for (ushort i = 0; i < axisCount; i++)
            {
                if (Native.Acm_AxOpen(_dev, i, out var ax) == 0 && ax != IntPtr.Zero)
                {
                    _axes[(short)i] = ax;
                }
            }

            if (!string.IsNullOrWhiteSpace(cfg))
            {
                _ = Native.Acm_DevLoadConfig(_dev, cfg);
            }

            return _axes.Count > 0;
        }, out error);
    }

    public override void Close()
    {
        foreach (var ax in _axes.Values)
        {
            try
            {
                var h = ax;
                _ = Native.Acm_AxClose(ref h);
            }
            catch (Exception)
            {
            }
        }

        _axes.Clear();
        if (_dev != IntPtr.Zero)
        {
            var h = _dev;
            try
            {
                _ = Native.Acm_DevClose(ref h);
            }
            catch (Exception)
            {
            }

            _dev = IntPtr.Zero;
        }
    }

    public override bool TryReadDiBit(int bit, out bool on)
    {
        on = false;
        if (Native.Acm_DaqDiGetByte(_dev, 0, out var b) != 0)
        {
            return false;
        }

        on = (b & (1 << (bit & 7))) != 0;
        return true;
    }

    public override bool WriteDoBit(int bit, bool on)
        => Native.Acm_DaqDoSetBit(_dev, (ushort)bit, (byte)(on ? 1 : 0)) == 0;

    public override bool EnableAxis(short axis, bool on)
        => TryAxis(axis, out var h) && Native.Acm_AxSetSvOn(h, on ? 1u : 0u) == 0;

    public override bool IsAxisEnabled(short axis) => _axes.ContainsKey(axis);

    public override bool TryGetPrfPos(short axis, out double pos)
    {
        pos = 0;
        return TryAxis(axis, out var h) && Native.Acm_AxGetCmdPosition(h, out pos) == 0;
    }

    public override bool TryGetEncPos(short axis, out double pos)
    {
        pos = 0;
        return TryAxis(axis, out var h) && Native.Acm_AxGetActualPosition(h, out pos) == 0;
    }

    public override bool MoveTrap(short axis, int target, double vel, double acc, double dec)
        => TryAxis(axis, out var h)
           && Native.Acm_AxChangeVel(h, Math.Abs(vel)) == 0
           && Native.Acm_AxMoveAbs(h, target) == 0;

    public override bool MoveJog(short axis, double vel, double acc, double dec)
        => TryAxis(axis, out var h) && Native.Acm_AxMoveVel(h, (ushort)(vel >= 0 ? 0 : 1)) == 0;

    public override bool MoveHome(short axis, short mode, double vel, double acc, double dec)
        => TryAxis(axis, out var h) && Native.Acm_AxHome(h, (uint)mode, 0) == 0;

    public override bool Stop(int axisMask, int option)
    {
        var ok = true;
        foreach (var (axis, h) in _axes)
        {
            if (axisMask != 0 && ((axisMask >> axis) & 1) == 0)
            {
                continue;
            }

            ok &= Native.Acm_AxStopDec(h) == 0;
        }

        return ok;
    }

    private bool TryAxis(short axis, out IntPtr handle) => _axes.TryGetValue(axis, out handle);

    private static class Native
    {
        private const string Dll = "ADVMOT.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint Acm_DevOpen(uint deviceNumber, out IntPtr deviceHandle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint Acm_DevClose(ref IntPtr deviceHandle);

        [DllImport(Dll, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint Acm_DevLoadConfig(IntPtr deviceHandle, string path);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint Acm_AxOpen(IntPtr deviceHandle, ushort phyAxis, out IntPtr axisHandle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint Acm_AxClose(ref IntPtr axisHandle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint Acm_AxSetSvOn(IntPtr axisHandle, uint onOff);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint Acm_AxMoveAbs(IntPtr axisHandle, double position);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint Acm_AxMoveVel(IntPtr axisHandle, ushort direction);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint Acm_AxStopDec(IntPtr axisHandle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint Acm_AxHome(IntPtr axisHandle, uint homeMode, uint dirMode);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint Acm_AxGetCmdPosition(IntPtr axisHandle, out double position);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint Acm_AxGetActualPosition(IntPtr axisHandle, out double position);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint Acm_AxChangeVel(IntPtr axisHandle, double newVel);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint Acm_DaqDiGetByte(IntPtr deviceHandle, ushort diPort, out byte data);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern uint Acm_DaqDoSetBit(IntPtr deviceHandle, ushort doChannel, byte bitData);
    }
}
