namespace MDKOSS.Core.Monitor;

/// <summary>Serial device debug page loader.</summary>
internal static class DebugSerialDevPage
{
    public static readonly string Html = ViewsHtml.Load("debug_serial.html", "debug_serial.html");
}
