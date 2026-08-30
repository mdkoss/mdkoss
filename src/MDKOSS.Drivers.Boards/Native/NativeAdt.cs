using System.Runtime.InteropServices;
using MDKOSS.Core;

namespace MDKOSS.Drivers.Boards;

/// <summary>众为兴 ADT-8948A1。轴号手册从 1 起；DLL 导出无前缀（pmove / read_bit）。</summary>
internal sealed class NativeAdt : NativeMotionBackend
{
    private int _card;
    private readonly HashSet<short> _enabled = [];

    public override string Vendor => "众为兴 ADT";

    public override bool TryOpen(MdkSetting.DriverConfig config, BoardKind kind, out string error)
    {
        _card = GetInt(config, "card", 0);
        return CatchNative(() => Native.adt8948_initial() > 0, out error);
    }

    public override void Close()
    {
        try
        {
            _ = Native.adt8948_end();
        }
        catch (Exception)
        {
        }

        _enabled.Clear();
    }

    public override bool TryReadDiBit(int bit, out bool on)
    {
        var rc = Native.read_bit(_card, bit);
        on = rc == 1;
        return rc is 0 or 1;
    }

    public override bool WriteDoBit(int bit, bool on) => Native.write_bit(_card, bit, on ? 1 : 0) == 0;

    public override bool EnableAxis(short axis, bool on)
    {
        // 8948 伺服使能走通用 DO；无独立 AxisOn。记录状态供 IsAxisEnabled。
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
        if (Native.get_command_pos(_card, NativeAxis(axis), out var p) != 0)
        {
            return false;
        }

        pos = p;
        return true;
    }

    public override bool TryGetEncPos(short axis, out double pos)
    {
        pos = 0;
        if (Native.get_actual_pos(_card, NativeAxis(axis), out var p) != 0)
        {
            return false;
        }

        pos = p;
        return true;
    }

    public override bool TryGetStatus(short axis, out int status)
    {
        status = 0;
        if (Native.get_status(_card, NativeAxis(axis), out var v) != 0)
        {
            return false;
        }

        status = v;
        return true;
    }

    public override bool SetPosition(short axis, double pos)
        => Native.set_command_pos(_card, NativeAxis(axis), (int)pos) == 0;

    public override bool SetVelocity(short axis, double vel)
        => Native.set_speed(_card, NativeAxis(axis), (int)Math.Abs(vel)) == 0;

    public override bool SetAcc(short axis, double acc)
        => Native.set_acc(_card, NativeAxis(axis), (int)Math.Abs(acc)) == 0;

    public override bool SetDec(short axis, double dec)
        => Native.set_dec(_card, NativeAxis(axis), (int)Math.Abs(dec)) == 0;

    public override bool MoveTrap(short axis, int target, double vel, double acc, double dec)
    {
        var n = NativeAxis(axis);
        return Native.set_startv(_card, n, (int)Math.Max(1, Math.Abs(vel) / 10)) == 0
               && Native.set_speed(_card, n, (int)Math.Abs(vel)) == 0
               && Native.set_acc(_card, n, (int)Math.Abs(acc)) == 0
               && Native.set_dec(_card, n, (int)Math.Abs(dec)) == 0
               && Native.pmove(_card, n, target) == 0;
    }

    public override bool MoveJog(short axis, double vel, double acc, double dec)
    {
        var n = NativeAxis(axis);
        var dir = vel >= 0 ? 0 : 1;
        return Native.set_speed(_card, n, (int)Math.Abs(vel)) == 0
               && Native.set_acc(_card, n, (int)Math.Abs(acc)) == 0
               && Native.continue_move(_card, n, dir) == 0;
    }

    public override bool Stop(int axisMask, int option)
    {
        var ok = true;
        for (short a = 0; a < 8; a++)
        {
            if (axisMask != 0 && ((axisMask >> a) & 1) == 0)
            {
                continue;
            }

            ok &= option == 0
                ? Native.dec_stop(_card, NativeAxis(a)) == 0
                : Native.sudden_stop(_card, NativeAxis(a)) == 0;
        }

        return ok;
    }

    private static int NativeAxis(short axis) => axis + 1;

    private static class Native
    {
        private const string Dll = "adt8948a1.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int adt8948_initial();

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int adt8948_end();

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int read_bit(int cardno, int number);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int write_bit(int cardno, int number, int value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int set_startv(int cardno, int axis, int value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int set_speed(int cardno, int axis, int value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int set_acc(int cardno, int axis, int value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int set_dec(int cardno, int axis, int value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int set_command_pos(int cardno, int axis, int value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int get_command_pos(int cardno, int axis, out int pos);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int get_actual_pos(int cardno, int axis, out int pos);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int get_status(int cardno, int axis, out int value);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int pmove(int cardno, int axis, int pulse);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int continue_move(int cardno, int axis, int dir);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int dec_stop(int cardno, int axis);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int sudden_stop(int cardno, int axis);
    }
}
