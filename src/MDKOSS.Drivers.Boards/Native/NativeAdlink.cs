using System.Runtime.InteropServices;
using MDKOSS.Core;

namespace MDKOSS.Drivers.Boards;

/// <summary>凌华 APS168。手册：APS_initial / APS_absolute_move / APS_write_d_channel_output。</summary>
internal sealed class NativeAdlink : NativeMotionBackend
{
    private int _board;
    private readonly HashSet<short> _enabled = [];

    public override string Vendor => "凌华 ADLINK";

    public override bool TryOpen(MdkSetting.DriverConfig config, BoardKind kind, out string error)
    {
        _board = GetInt(config, "card", 0);
        var bits = 1 << _board;
        return CatchNative(() => Native.APS_initial(bits, 0) >= 0, out error);
    }

    public override void Close()
    {
        try
        {
            _ = Native.APS_close();
        }
        catch (Exception)
        {
        }

        _enabled.Clear();
    }

    public override bool TryReadDiBit(int bit, out bool on)
    {
        on = false;
        if (Native.APS_read_d_channel_input(_board, bit, out var v) != 0)
        {
            return false;
        }

        on = v != 0;
        return true;
    }

    public override bool WriteDoBit(int bit, bool on)
        => Native.APS_write_d_channel_output(_board, bit, on ? 1 : 0) == 0;

    public override bool EnableAxis(short axis, bool on)
    {
        if (Native.APS_set_servo_on(axis, on ? 1 : 0) != 0)
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
        if (Native.APS_get_command(axis, out var p) != 0)
        {
            return false;
        }

        pos = p;
        return true;
    }

    public override bool TryGetEncPos(short axis, out double pos)
    {
        pos = 0;
        if (Native.APS_get_position(axis, out var p) != 0)
        {
            return false;
        }

        pos = p;
        return true;
    }

    public override bool SetPosition(short axis, double pos) => Native.APS_set_command(axis, (int)pos) == 0;

    public override bool MoveTrap(short axis, int target, double vel, double acc, double dec)
        => Native.APS_absolute_move(axis, target, (int)Math.Max(1, Math.Abs(vel))) == 0;

    public override bool MoveJog(short axis, double vel, double acc, double dec)
    {
        var speed = (int)Math.Max(1, Math.Abs(vel));
        return Native.APS_velocity_move(axis, vel >= 0 ? speed : -speed) == 0;
    }

    public override bool MoveHome(short axis, short mode, double vel, double acc, double dec)
        => Native.APS_home_move(axis) == 0;

    public override bool Stop(int axisMask, int option)
    {
        var ok = true;
        for (short a = 0; a < 16; a++)
        {
            if (axisMask != 0 && ((axisMask >> a) & 1) == 0)
            {
                continue;
            }

            ok &= Native.APS_stop_move(a) == 0;
        }

        return ok;
    }

    private static class Native
    {
        private const string Dll = "APS168.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int APS_initial(int boardIdInBits, int mode);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int APS_close();

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int APS_set_servo_on(int axisId, int onOff);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int APS_get_command(int axisId, out int pos);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int APS_get_position(int axisId, out int pos);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int APS_set_command(int axisId, int pos);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int APS_absolute_move(int axisId, int position, int maxSpeed);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int APS_velocity_move(int axisId, int maxSpeed);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int APS_home_move(int axisId);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int APS_stop_move(int axisId);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int APS_write_d_channel_output(int boardId, int channel, int onOff);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int APS_read_d_channel_input(int boardId, int channel, out int data);
    }
}
