using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using MDKOSS.Core;

namespace MDKOSS.Drivers.Boards;

/// <summary>Galil gclib。GOpen / GCommand（PA/BG/JG/ST/SH/MO、@IN/@OUT）。</summary>
internal sealed class NativeGalil : NativeMotionBackend
{
    private IntPtr _g;
    private readonly HashSet<short> _enabled = [];

    public override string Vendor => "Galil";

    public override bool TryOpen(MdkSetting.DriverConfig config, BoardKind kind, out string error)
    {
        var ip = GetString(config, "ip", "192.168.1.2");
        var address = ip.Contains(' ') ? ip : ip + " --direct";
        return CatchNative(() => Native.GOpen(address, out _g) == 0 && _g != IntPtr.Zero, out error);
    }

    public override void Close()
    {
        if (_g == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _ = Native.GClose(_g);
        }
        catch (Exception)
        {
        }

        _g = IntPtr.Zero;
        _enabled.Clear();
    }

    public override bool TryReadDiBit(int bit, out bool on)
    {
        on = false;
        if (!TryCmd($"MG @IN[{bit}]", out var n))
        {
            return false;
        }

        on = n != 0;
        return true;
    }

    public override bool TryReadDoBit(int bit, out bool on)
    {
        on = false;
        if (!TryCmd($"MG @OUT[{bit}]", out var n))
        {
            return false;
        }

        on = n != 0;
        return true;
    }

    public override bool WriteDoBit(int bit, bool on) => Cmd(on ? $"SB {bit}" : $"CB {bit}");

    public override bool EnableAxis(short axis, bool on)
    {
        var letter = Letter(axis);
        if (!Cmd(on ? "SH" + letter : "MO" + letter))
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
        => TryCmd($"MG _TP{Letter(axis)}", out pos);

    public override bool SetPosition(short axis, double pos) => Cmd($"DP{Letter(axis)}={ToInt(pos)}");

    public override bool MoveTrap(short axis, int target, double vel, double acc, double dec)
    {
        var a = Letter(axis);
        return Cmd($"SP{a}={ToInt(vel)};AC{a}={ToInt(acc)};DC{a}={ToInt(dec)};PA{a}={target};BG{a}");
    }

    public override bool MoveJog(short axis, double vel, double acc, double dec)
    {
        var a = Letter(axis);
        return Cmd($"AC{a}={ToInt(acc)};DC{a}={ToInt(dec)};JG{a}={ToInt(vel)};BG{a}");
    }

    public override bool MoveHome(short axis, short mode, double vel, double acc, double dec)
    {
        var a = Letter(axis);
        return Cmd($"SP{a}={ToInt(vel)};HM{a};BG{a}");
    }

    public override bool Stop(int axisMask, int option)
    {
        if (axisMask == 0)
        {
            return Cmd("ST");
        }

        var ok = true;
        for (short i = 0; i < 8; i++)
        {
            if (((axisMask >> i) & 1) == 0)
            {
                continue;
            }

            ok &= Cmd("ST" + Letter(i));
        }

        return ok;
    }

    private bool Cmd(string command) => TryCmd(command, out _);

    private bool TryCmd(string command, out double value)
    {
        value = 0;
        lock (Gate)
        {
            var buf = new StringBuilder(256);
            if (Native.GCommand(_g, command, buf, (uint)buf.Capacity, out _) != 0)
            {
                return false;
            }

            var text = buf.ToString().Trim().TrimEnd(':');
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    private static string Letter(short axis) => ((char)('A' + Math.Clamp((int)axis, 0, 7))).ToString();

    private static int ToInt(double v) => (int)Math.Round(v);

    private static class Native
    {
        private const string Dll = "gclib.dll";

        [DllImport(Dll, EntryPoint = "GOpen", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int GOpen(string address, out IntPtr g);

        [DllImport(Dll, EntryPoint = "GClose", CallingConvention = CallingConvention.StdCall)]
        public static extern int GClose(IntPtr g);

        [DllImport(Dll, EntryPoint = "GCommand", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern int GCommand(IntPtr g, string command, StringBuilder buffer, uint bufferLen, out uint bytesReturned);
    }
}
