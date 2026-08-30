using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.UI.WPF.Infrastructure;

public static class DeviceKind
{
    public static bool IsAxis(DeviceSnapshot d) =>
        d.AxisStatus is not null
        || d.Type is "axis" or "linear" or "rotary";

    public static bool IsPlatform(DeviceSnapshot d) =>
        d.PlatformAxes is { Count: > 0 }
        || d.Type.Contains("xy", StringComparison.OrdinalIgnoreCase)
        || d.Type.Contains("platform", StringComparison.OrdinalIgnoreCase);

    public static bool IsCamera(DeviceSnapshot d) =>
        d.Type is "cameradev" or "camera" or "extcamera";

    public static bool IsVision(DeviceSnapshot d) =>
        d.Type is "visiondev" or "vision";

    public static bool IsGpio(DeviceSnapshot d) =>
        d.GpioIoPoints is { Count: > 0 }
        || d.Type is "gpio" or "vio";

    public static bool IsSerial(DeviceSnapshot d) =>
        d.SerialPortInfo is not null || d.Type is "serialdev";

    public static bool IsMysql(DeviceSnapshot d) =>
        d.Type is "mysqldev";

    public static bool IsTcp(DeviceSnapshot d) =>
        d.Type is "tcpdev";

    public static bool IsPyScript(DeviceSnapshot d) =>
        d.Type is "devpyscript";

    public static bool IsModServer(DeviceSnapshot d) =>
        d.Type is "devmodserver";

    public static bool IsModClient(DeviceSnapshot d) =>
        d.Type is "devmodclient";

    public static bool IsModbus(DeviceSnapshot d) =>
        IsModServer(d) || IsModClient(d);

    public static bool IsSampleBeacon(DeviceSnapshot d) =>
        d.Type is "samplebeacon";

    public static bool IsIoOut(string direction) =>
        direction is "out" or "do" or "vio";

    public static string OnlineText(bool online) => online ? "在线" : "离线";

    public static string EnabledText(bool? enabled) =>
        enabled == true ? "使能" : "未使能";

    public static string Fmt(double? n, string fallback = "—") =>
        n is null ? fallback : n.Value.ToString("0.###");

    public static string AxisFlags(AxisStatus? st)
    {
        if (st is null)
        {
            return "—";
        }

        var flags = new List<string>();
        if (st.Value.ServoOn) flags.Add("Servo");
        if (st.Value.Moving) flags.Add("Moving");
        if (st.Value.InPosition) flags.Add("InPos");
        if (st.Value.Home) flags.Add("Home");
        if (st.Value.Alarm) flags.Add("Alarm");
        if (st.Value.PositiveLimit) flags.Add("+EL");
        if (st.Value.NegativeLimit) flags.Add("-EL");
        return flags.Count == 0 ? "—" : string.Join(" ", flags);
    }

    public static bool ConfirmWrite(string message) =>
        System.Windows.MessageBox.Show(
            message,
            "确认写入",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.OK;
}
