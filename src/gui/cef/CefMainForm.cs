using CefSharp.WinForms;
using MDKOSS.Core;

namespace MDKOSS.Gui.CefUi;

public sealed class CefMainForm : Form
{
    public CefMainForm(MdkRuntime runtime)
    {
        Text = $"MDKOSS - {runtime.Setting.ProjectName}";
        Width = 1280;
        Height = 800;
        StartPosition = FormStartPosition.CenterScreen;

        var browser = new ChromiumWebBrowser(ResolveIndexUrl())
        {
            Dock = DockStyle.Fill
        };
        Controls.Add(browser);
    }

    private static string ResolveIndexUrl()
    {
        var indexPath = Path.Combine(AppContext.BaseDirectory, "views", "index.html");
        if (!File.Exists(indexPath))
        {
            return "about:blank";
        }

        return new Uri(indexPath).AbsoluteUri;
    }
}
