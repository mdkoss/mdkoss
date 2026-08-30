using System.Runtime.InteropServices;
using MDKOSS.Core;

namespace MDKOSS.Drivers.Boards;

/// <summary>摩信 / 乐创 MPC08。手册：auto_set + init_board；通道号从 1 起。</summary>
internal sealed class NativeMpc : NativeMotionBackend
{
    private int _card;
    private readonly HashSet<short> _enabled = [];

    public override string Vendor => "摩信 MPC";

    public override bool TryOpen(MdkSetting.DriverConfig config, BoardKind kind, out string error)
    {
        _card = GetInt(config, "card", 0);
        return CatchNative(() => Native.auto_set() > 0 && Native.init_board() > 0, out error);
    }

    public override void Close() => _enabled.Clear();

    public override bool TryReadDiBit(int bit, out bool on)
    {
        var rc = Native.checkin_bit(_card, bit);
        on = rc == 1;
        return rc is 0 or 1;
    }

    public override bool WriteDoBit(int bit, bool on) => Native.outport_bit(_card, bit, on ? 1 : 0) == 0;

    public override bool EnableAxis(short axis, bool on)
    {
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
        if (Native.get_abs_pos(NativeCh(axis), out var p) != 0)
        {
            return false;
        }

        pos = p;
        return true;
    }

    public override bool TryGetEncPos(short axis, out double pos)
    {
        pos = 0;
        if (Native.get_encoder(NativeCh(axis), out var p) != 0)
        {
            return false;
        }

        pos = p;
        return true;
    }

    public override bool TryGetStatus(short axis, out int status)
    {
        status = Native.check_status(NativeCh(axis));
        return true;
    }

    public override bool SetPosition(short axis, double pos) => Native.set_abs_pos(NativeCh(axis), (int)pos) == 0;

    public override bool SetVelocity(short axis, double vel)
        => Native.set_profile(NativeCh(axis), Math.Abs(vel) / 4, Math.Abs(vel), 80000) == 0;

    public override bool MoveTrap(short axis, int target, double vel, double acc, double dec)
    {
        var ch = NativeCh(axis);
        var hs = Math.Max(1, Math.Abs(vel));
        return Native.set_profile(ch, hs / 4, hs, Math.Max(1, Math.Abs(acc))) == 0
               && Native.fast_pmove(ch, target) == 0;
    }

    public override bool MoveJog(short axis, double vel, double acc, double dec)
    {
        var ch = NativeCh(axis);
        var dir = vel >= 0 ? 1 : -1;
        return Native.set_conspeed(ch, Math.Abs(vel)) == 0 && Native.con_vmove(ch, dir) == 0;
    }

    public override bool MoveHome(short axis, short mode, double vel, double acc, double dec)
    {
        var ch = NativeCh(axis);
        return Native.set_home_mode(ch, mode) == 0 && Native.home_move(ch) == 0;
    }

    public override bool Stop(int axisMask, int option)
    {
        var ok = true;
        for (short a = 0; a < 16; a++)
        {
            if (axisMask != 0 && ((axisMask >> a) & 1) == 0)
            {
                continue;
            }

            ok &= Native.sudden_stop(NativeCh(a)) == 0;
        }

        return ok;
    }

    private static int NativeCh(short axis) => axis + 1;

    private static class Native
    {
        private const string Dll = "MPC08.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int auto_set();

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int init_board();

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int checkin_bit(int cardno, int bitno);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int outport_bit(int cardno, int bitno, int status);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int set_profile(int ch, double ls, double hs, double acc);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int set_conspeed(int ch, double conspeed);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int fast_pmove(int ch, int step);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int con_vmove(int ch, int dir);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int sudden_stop(int ch);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int get_abs_pos(int ch, out int pos);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int get_encoder(int ch, out int pos);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int set_abs_pos(int ch, int pos);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int check_status(int ch);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int set_home_mode(int ch, int homeMode);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int home_move(int ch);
    }
}
