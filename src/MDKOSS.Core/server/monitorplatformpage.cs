namespace MDKOSS.Core.Monitor;

/// <summary>Platform jog / step-teach debug page loader.</summary>
internal static class MonitorPlatformPage
{
    public static readonly string Html = ViewsHtml.Load("debug_platform.html", "debug_platform.html");
}
