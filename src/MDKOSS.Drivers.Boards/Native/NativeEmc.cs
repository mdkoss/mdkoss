using System.Runtime.InteropServices;
using MDKOSS.Core;

namespace MDKOSS.Drivers.Boards;

/// <summary>雷赛 EtherCAT / 总线 LTDMC。函数签名与 <see cref="DrvDmc"/> 相同，仅占用 <c>emc</c> type。</summary>
internal sealed class NativeEmc : NativeMotionBackend
{
    private ushort _card;
    private bool _sevonActiveLow = true;
    private readonly HashSet<short> _enabled = [];

    public override string Vendor => "雷赛 EtherCAT";

    public override bool TryOpen(MdkSetting.DriverConfig config, BoardKind kind, out string error)
    {
        _card = (ushort)GetInt(config, "card", 0);
        _sevonActiveLow = GetInt(config, "sevonActiveLow", 1) != 0
                          || string.Equals(GetString(config, "sevonActiveLow", "true"), "true", StringComparison.OrdinalIgnoreCase);
        return CatchNative(() => Native.dmc_board_init() > 0, out error);
    }

    public override void Close()
    {
        try
        {
            _ = Native.dmc_board_close();
        }
        catch (Exception)
        {
        }

        _enabled.Clear();
    }

    public override bool TryReadDiBit(int bit, out bool on)
    {
        var rc = Native.dmc_read_inbit(_card, (ushort)bit);
        on = rc == 1;
        return rc is 0 or 1;
    }

    public override bool TryReadDoBit(int bit, out bool on)
    {
        var rc = Native.dmc_read_outbit(_card, (ushort)bit);
        on = rc == 1;
        return rc is 0 or 1;
    }

    public override bool WriteDoBit(int bit, bool on)
        => Native.dmc_write_outbit(_card, (ushort)bit, (ushort)(on ? 1 : 0)) == 0;

    public override bool EnableAxis(short axis, bool on)
    {
        var level = (ushort)(_sevonActiveLow ? on ? 0 : 1 : on ? 1 : 0);

        if (Native.dmc_write_sevon_pin(_card, (ushort)axis, level) != 0)
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
        pos = Native.dmc_get_position(_card, (ushort)axis);
        return true;
    }

    public override bool TryGetEncPos(short axis, out double pos)
    {
        pos = Native.dmc_get_encoder(_card, (ushort)axis);
        return true;
    }

    public override bool SetPosition(short axis, double pos)
        => Native.dmc_set_position(_card, (ushort)axis, (int)pos) == 0;

    public override bool MoveTrap(short axis, int target, double vel, double acc, double dec)
        => Native.dmc_set_profile(_card, (ushort)axis, 0, Math.Abs(vel), Math.Abs(acc), Math.Abs(dec), 0) == 0
           && Native.dmc_pmove(_card, (ushort)axis, target, posi_mode: 1) == 0;

    public override bool MoveJog(short axis, double vel, double acc, double dec)
        => Native.dmc_set_profile(_card, (ushort)axis, 0, Math.Abs(vel), Math.Abs(acc), Math.Abs(dec), 0) == 0
           && Native.dmc_vmove(_card, (ushort)axis, (ushort)(vel >= 0 ? 1 : 0)) == 0;

    public override bool MoveHome(short axis, short mode, double vel, double acc, double dec)
        => Native.dmc_home_move(_card, (ushort)axis) == 0;

    public override bool Stop(int axisMask, int option)
    {
        var ok = true;
        for (short a = 0; a < 16; a++)
        {
            if (axisMask != 0 && ((axisMask >> a) & 1) == 0)
            {
                continue;
            }

            ok &= Native.dmc_stop(_card, (ushort)a, (ushort)(option == 0 ? 0 : 1)) == 0;
        }

        return ok;
    }

    private static class Native
    {
        private const string Dll = "LTDMC.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern short dmc_board_init();

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern short dmc_board_close();

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern short dmc_read_inbit(ushort card, ushort bitno);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern short dmc_read_outbit(ushort card, ushort bitno);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern short dmc_write_outbit(ushort card, ushort bitno, ushort onOff);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern short dmc_write_sevon_pin(ushort card, ushort axis, ushort onOff);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern short dmc_set_profile(ushort card, ushort axis, double minVel, double maxVel, double tacc, double tdec, double stopVel);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern short dmc_pmove(ushort card, ushort axis, int dist, ushort posi_mode);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern short dmc_vmove(ushort card, ushort axis, ushort dir);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern short dmc_home_move(ushort card, ushort axis);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int dmc_get_position(ushort card, ushort axis);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int dmc_get_encoder(ushort card, ushort axis);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern short dmc_set_position(ushort card, ushort axis, int current);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern short dmc_stop(ushort card, ushort axis, ushort stopMode);
    }
}
