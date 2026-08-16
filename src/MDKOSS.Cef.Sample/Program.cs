using MDKOSS.Core;
using MDKOSS.Extensions;
using MDKOSS.Gui.CefUi;
using MDKOSS.Host;
using System.Windows.Forms;

namespace MDKOSS.Cef.Sample;

/// <summary>
/// CEF host that loads and runs the first JSON in <c>configs/</c>.
/// Start page, devices, and tasks come from the setting file.
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
        var version = MdkProduct.Version;
        AppLog.Info($"MDKOSS.Cef.Sample starting (version: {version})");

        ApplicationConfiguration.Initialize();

        const string appTitle = "MDKOSS CEF Sample";

        if (!File.Exists(settingPath))
        {
            AppLog.Error($"Setting file not found: {settingPath}");
            MessageBox.Show(
                $"Setting file not found:\n{settingPath}\n\nExpected: a JSON file under configs/ next to the exe.",
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

        var startPath = RuntimeHost.ResolveStartPage(setting);

        MdkRuntime runtime;
        try
        {
            runtime = new MdkRuntime(setting, settingPath);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Failed to create runtime");
            MessageBox.Show(
                ex.Message,
                appTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        using (runtime)
        {
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
}
