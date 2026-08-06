using MDKOSS.Core;
using MDKOSS.Extensions;
using MDKOSS.Gui.CefUi;
using MDKOSS.Host;
using System.Windows.Forms;

namespace MDKOSS.Cef.Sample;

/// <summary>
/// Lightweight CEF host that opens <c>index.html</c> to exercise core HMI pages
/// (popup / monitor / debug / man) without DieBonder / PNP machine plugins.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        AppLog.Configure();
        MdkExtensionHost.DiscoverAndRegister(new ExtensionDiscoveryOptions
        {
            Log = msg => AppLog.Info(msg),
        });

        var settingPath = RuntimeHost.ResolveSettingPath(args);
        RunCefDesktop(settingPath);
    }

    private static void RunCefDesktop(string settingPath)
    {
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        AppLog.Info($"MDKOSS.Cef.Sample starting (version: {version})");

        ApplicationConfiguration.Initialize();

        const string appTitle = "MDKOSS CEF HMI — index.html";

        if (!File.Exists(settingPath))
        {
            AppLog.Error($"Setting file not found: {settingPath}");
            MessageBox.Show(
                $"Setting file not found:\n{settingPath}\n\nExpected: configs/sample.setting.json next to the exe.",
                appTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (!RuntimeHost.TryLoadSettings(settingPath, out var setting))
        {
            MessageBox.Show(
                "Failed to load settings. See logs for details.",
                appTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        // Force core HMI entry for this sample (config may still declare startPage).
        setting.StartPage = "index.html";

        using var runtime = new MdkRuntime(setting, settingPath);
        if (!RuntimeHost.TryBootstrapRuntime(runtime, out var startupError))
        {
            MessageBox.Show(
                startupError ?? "Startup failed.",
                appTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (!CefRuntimeBootstrap.TryInitialize(out var cefError))
        {
            AppLog.Error($"CEF init failed: {cefError}");
            MessageBox.Show(
                cefError ?? "Failed to initialize CEF.",
                appTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            RuntimeHost.ShutdownRuntime(runtime);
            return;
        }

        try
        {
            var startUrl = CefMainForm.ResolveStartUrl(runtime, "index.html");
            AppLog.Info($"CEF UI starting ({startUrl})");
            Application.Run(new CefMainForm(runtime, "index.html"));
            AppLog.Info("CEF UI closed");
        }
        finally
        {
            CefRuntimeBootstrap.Shutdown();
            RuntimeHost.ShutdownRuntime(runtime);
        }
    }
}
