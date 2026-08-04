using CefSharp.WinForms;
using MDKOSS.Core;

namespace MDKOSS.Gui.CefUi;

public sealed class CefMainForm : Form
{
    public CefMainForm(MdkRuntime runtime, string? startPath = null)
    {
        Text = $"MDKOSS - {runtime.Setting.ProjectName}";
        Width = 1440;
        Height = 900;
        StartPosition = FormStartPosition.CenterScreen;

        var startUrl = ResolveStartUrl(runtime, startPath);
        var browser = new ChromiumWebBrowser(startUrl)
        {
            Dock = DockStyle.Fill
        };
        Controls.Add(browser);
    }

    /// <summary>
    /// Prefer the monitoring HTTP prefix so relative <c>/api/*</c> calls work inside CEF.
    /// </summary>
    public static string ResolveStartUrl(MdkRuntime runtime, string? startPath = null)
    {
        var path = string.IsNullOrWhiteSpace(startPath) ? "index.html" : startPath.Trim();
        path = path.TrimStart('/');
        var prefix = runtime.MonitoringPrefix;
        if (!prefix.EndsWith('/'))
        {
            prefix += "/";
        }

        return prefix + path;
    }
}
