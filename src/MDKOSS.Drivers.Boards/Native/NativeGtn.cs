using System.Runtime.InteropServices;
using MDKOSS.Core;

namespace MDKOSS.Drivers.Boards;

/// <summary>固高 GTN / GLink。指令与 GTS 同族，首参为 core；轴号从 1 起。</summary>
internal sealed class NativeGtn : NativeMotionBackend
{
    private short _core = 1;
    private readonly HashSet<short> _enabled = [];

    public override string Vendor => "固高 GTN";

    public override bool TryOpen(MdkSetting.DriverConfig config, BoardKind kind, out string error)
    {
        _core = (short)GetInt(config, "core", 1);
        if (_core <= 0)
        {
            _core = 1;
        }

        var reset = GetInt(config, "resetOnInit", 0) != 0
                    || string.Equals(GetString(config, "resetOnInit", "false"), "true", StringComparison.OrdinalIgnoreCase);
        return CatchNative(() =>
        {
            if (Native.GTN_Open() != 0)
            {
                return false;
            }

            if (reset)
            {
                _ = Native.GTN_Reset(_core);
            }

            return true;
        }, out error);
    }

    public override void Close()
    {
        try
        {
            _ = Native.GTN_Close();
        }
        catch (Exception)
        {
        }

        _enabled.Clear();
    }

    public override bool TryReadDiPort(out int word)
    {
        word = 0;
        return Native.GTN_GetDi(_core, 4, out word) == 0;
    }

    public override bool TryReadDoPort(out int word)
    {
        word = 0;
        return Native.GTN_GetDo(_core, 12, out word) == 0;
    }

    public override bool WriteDoPort(int word) => Native.GTN_SetDo(_core, 12, word) == 0;

    public override bool WriteDoBit(int bit, bool on)
        => Native.GTN_SetDoBit(_core, 12, (short)Math.Max(1, bit), (short)(on ? 1 : 0)) == 0;

    public override bool TryReadDiBit(int bit, out bool on)
    {
        on = false;
        if (!TryReadDiPort(out var word))
        {
            return false;
        }

        var idx = bit < 1 ? 0 : bit - 1;
        on = (word & (1 << idx)) != 0;
        return true;
    }

    public override bool EnableAxis(short axis, bool on)
    {
        var n = NativeAxis(axis);
        var ok = on ? Native.GTN_AxisOn(_core, n) == 0 : Native.GTN_AxisOff(_core, n) == 0;
        if (ok)
        {
            if (on)
            {
                _enabled.Add(axis);
            }
            else
            {
                _enabled.Remove(axis);
            }
        }

        return ok;
    }

    public override bool IsAxisEnabled(short axis) => _enabled.Contains(axis);

    public override bool TryGetPrfPos(short axis, out double pos)
    {
        pos = 0;
        return Native.GTN_GetPrfPos(_core, NativeAxis(axis), out pos, 1, out _) == 0;
    }

    public override bool TryGetEncPos(short axis, out double pos)
    {
        pos = 0;
        return Native.GTN_GetEncPos(_core, NativeAxis(axis), out pos, 1, out _) == 0;
    }

    public override bool TryGetStatus(short axis, out int status)
    {
        status = 0;
        return Native.GTN_GetSts(_core, NativeAxis(axis), out status, 1, out _) == 0;
    }

    public override bool TryGetVel(short axis, out double vel)
    {
        vel = 0;
        return Native.GTN_GetVel(_core, NativeAxis(axis), out vel) == 0;
    }

    public override bool SetPosition(short axis, double pos)
        => Native.GTN_SetPrfPos(_core, NativeAxis(axis), (int)pos) == 0;

    public override bool SetVelocity(short axis, double vel)
        => Native.GTN_SetVel(_core, NativeAxis(axis), vel) == 0;

    public override bool MoveTrap(short axis, int target, double vel, double acc, double dec)
    {
        var n = NativeAxis(axis);
        var trap = new Native.TTrapPrm { acc = acc, dec = dec, velStart = 0, smoothTime = 0 };
        if (Native.GTN_PrfTrap(_core, n) != 0
            || Native.GTN_SetTrapPrm(_core, n, ref trap) != 0
            || Native.GTN_SetPos(_core, n, target) != 0
            || Native.GTN_SetVel(_core, n, vel) != 0)
        {
            return false;
        }

        var mask = 1 << (n - 1);
        return Native.GTN_Update(_core, mask) == 0;
    }

    public override bool MoveJog(short axis, double vel, double acc, double dec)
    {
        var n = NativeAxis(axis);
        var jog = new Native.TJogPrm { acc = acc, dec = dec, smooth = 0 };
        if (Native.GTN_PrfJog(_core, n) != 0
            || Native.GTN_SetJogPrm(_core, n, ref jog) != 0
            || Native.GTN_SetVel(_core, n, vel) != 0)
        {
            return false;
        }

        return Native.GTN_Update(_core, 1 << (n - 1)) == 0;
    }

    public override bool Stop(int axisMask, int option)
        => Native.GTN_Stop(_core, axisMask == 0 ? -1 : axisMask, option) == 0;

    private static short NativeAxis(short axis) => (short)(axis < 1 ? axis + 1 : axis);

    private static class Native
    {
        private const string Dll = "gtn.dll";

        [StructLayout(LayoutKind.Sequential)]
        public struct TTrapPrm
        {
            public double acc;
            public double dec;
            public double velStart;
            public short smoothTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TJogPrm
        {
            public double acc;
            public double dec;
            public double smooth;
        }

        [DllImport(Dll)]
        public static extern short GTN_Open();

        [DllImport(Dll)]
        public static extern short GTN_Close();

        [DllImport(Dll)]
        public static extern short GTN_Reset(short core);

        [DllImport(Dll)]
        public static extern short GTN_GetDi(short core, short diType, out int pValue);

        [DllImport(Dll)]
        public static extern short GTN_GetDo(short core, short doType, out int pValue);

        [DllImport(Dll)]
        public static extern short GTN_SetDo(short core, short doType, int value);

        [DllImport(Dll)]
        public static extern short GTN_SetDoBit(short core, short doType, short doIndex, short value);

        [DllImport(Dll)]
        public static extern short GTN_AxisOn(short core, short axis);

        [DllImport(Dll)]
        public static extern short GTN_AxisOff(short core, short axis);

        [DllImport(Dll)]
        public static extern short GTN_GetSts(short core, short axis, out int pStatus, short count, out uint pClock);

        [DllImport(Dll)]
        public static extern short GTN_GetPrfPos(short core, short profile, out double pValue, short count, out uint pClock);

        [DllImport(Dll)]
        public static extern short GTN_GetEncPos(short core, short profile, out double pValue, short count, out uint pClock);

        [DllImport(Dll)]
        public static extern short GTN_GetVel(short core, short profile, out double pValue);

        [DllImport(Dll)]
        public static extern short GTN_SetPrfPos(short core, short profile, int pos);

        [DllImport(Dll)]
        public static extern short GTN_SetVel(short core, short profile, double vel);

        [DllImport(Dll)]
        public static extern short GTN_PrfTrap(short core, short profile);

        [DllImport(Dll)]
        public static extern short GTN_SetTrapPrm(short core, short profile, ref TTrapPrm pPrm);

        [DllImport(Dll)]
        public static extern short GTN_SetPos(short core, short profile, int pos);

        [DllImport(Dll)]
        public static extern short GTN_PrfJog(short core, short profile);

        [DllImport(Dll)]
        public static extern short GTN_SetJogPrm(short core, short profile, ref TJogPrm pPrm);

        [DllImport(Dll)]
        public static extern short GTN_Update(short core, int mask);

        [DllImport(Dll)]
        public static extern short GTN_Stop(short core, int mask, int option);
    }
}
