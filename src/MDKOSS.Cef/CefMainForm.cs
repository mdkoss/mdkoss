using CefSharp.WinForms;
using MDKOSS.Core;

namespace MDKOSS.Gui.CefUi;

public sealed class CefMainForm : Form
{
    public CefMainForm(MdkRuntime runtime, string? startPath = null)
    {
        Text = $"MDKOSS - {runtime.Setting.ProjectName}";
        Width = 1920;
        Height = 1080;
        StartPosition = FormStartPosition.CenterScreen;
        try
        {
            // Prefer each host exe's ApplicationIcon (Sample / Cef.Sample differ).
            var exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (exeIcon != null)
            {
                Icon = exeIcon;
            }
        }
        catch
        {
            // Keep default Form icon if extraction fails.
        }

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
