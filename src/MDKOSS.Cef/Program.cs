using MDKOSS.Core;
using MDKOSS.Extensions;
using MDKOSS.Gui.CefUi;
using MDKOSS.Host;
using System.Windows.Forms;

namespace MDKOSS.CefHost;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        AppLog.Configure();
        MdkExtensionHost.DiscoverAndRegister(new ExtensionDiscoveryOptions
        {
            Log = msg => AppLog.Info(msg),
        });

        var settingPath = RuntimeHost.ResolveSettingPath(args);
        if (args.Any(a => string.Equals(a, "--console", StringComparison.OrdinalIgnoreCase)))
        {
            await RuntimeHost.RunConsoleRuntimeAsync(settingPath).ConfigureAwait(false);
            return;
        }

        RunCefDesktop(settingPath);
    }

    private static void RunCefDesktop(string settingPath)
    {
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        AppLog.Info($"MDKOSS.Cef starting (version: {version})================================================");

        ApplicationConfiguration.Initialize();

        if (!File.Exists(settingPath))
        {
            AppLog.Error($"Setting file not found: {settingPath}");
            MessageBox.Show(
                $"Setting file not found:\n{settingPath}",
                "MDKOSS.Cef",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (!RuntimeHost.TryLoadSettings(settingPath, out var setting))
        {
            MessageBox.Show(
                "Failed to load settings. See logs for details.",
                "Settings Load Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        using var runtime = new MdkRuntime(setting);
        if (!RuntimeHost.TryBootstrapRuntime(runtime, out var startupError))
        {
            MessageBox.Show(
                startupError ?? "Startup failed.",
                "Startup Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        var startPath = RuntimeHost.IsPnpProject(setting, settingPath) ? "indexPnp.html" : "index.html";
        if (!CefRuntimeBootstrap.TryInitialize(out var cefError))
        {
            AppLog.Error($"CEF init failed: {cefError}");
            MessageBox.Show(
                cefError ?? "Failed to initialize CEF.",
                "CEF Startup Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            RuntimeHost.ShutdownRuntime(runtime);
            return;
        }

        try
        {
            var startUrl = CefMainForm.ResolveStartUrl(runtime, startPath);
            AppLog.Info($"CEF UI starting ({startUrl})");
            Application.Run(new CefMainForm(runtime, startPath));
            AppLog.Info("CEF UI closed");
        }
        finally
        {
            CefRuntimeBootstrap.Shutdown();
            RuntimeHost.ShutdownRuntime(runtime);
        }
    }
}
