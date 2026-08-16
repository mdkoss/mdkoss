using MDKOSS.Core;
using MDKOSS.Extensions;
using MDKOSS.Gui.CefUi;
using MDKOSS.Host;
using MDKOSS.Pnp;
using MDKOSS.Sample.DieBonder.Machine;
using System.Windows.Forms;

namespace MDKOSS.Sample.DieBonder;

/// <summary>
/// CEF host for the semiconductor die bonder sample. Devices and the start page come from
/// the first JSON in <c>configs/</c>; machine tasks / API are registered here.
/// </summary>
internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        AppLog.Configure();
        // Force ProjectReference MDKOSS.Sample.Pnp into Default ALC before plugin scan so
        // DieBonder `TryGetDevice<TrayDevice>` matches devices created by the Pnp extension.
        _ = typeof(TrayDevice);

        MdkExtensionHost.DiscoverAndRegister(new ExtensionDiscoveryOptions
        {
            Log = msg => AppLog.Info(msg),
        });
        MdkExtensionHost.Register(new DieBonderExtension());

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
        var version = MdkProduct.Version;
        AppLog.Info($"MDKOSS.Sample.DieBonder starting (version: {version})================================================");

        ApplicationConfiguration.Initialize();

        const string appTitle = "MDKOSS Sample — 半导体贴片机";

        if (!File.Exists(settingPath))
        {
            AppLog.Error($"Setting file not found: {settingPath}");
            MessageBox.Show(
                $"Setting file not found:\n{settingPath}",
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

        var startPath = RuntimeHost.ResolveStartPage(setting);
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
