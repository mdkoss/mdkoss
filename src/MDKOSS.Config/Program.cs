using MDKOSS.Core;
using MDKOSS.Extensions;
using MDKOSS.Gui;
using MDKOSS.Host;
using System.Windows.Forms;

namespace MDKOSS.Config;

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
        RunConfigDesktop(settingPath);
    }

    private static void RunConfigDesktop(string settingPath)
    {
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        AppLog.Info($"MDKOSS.Config starting (version: {version})================================================");

        ApplicationConfiguration.Initialize();

        if (!File.Exists(settingPath))
        {
            AppLog.Error($"Setting file not found: {settingPath}");
            MessageBox.Show(
                $"Setting file not found:\n{settingPath}",
                "MDKOSS.Config",
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

        try
        {
            AppLog.Info("WinForms config UI starting");
            Application.Run(new MainForm(runtime, settingPath));
            AppLog.Info("WinForms config UI closed");
        }
        finally
        {
            RuntimeHost.ShutdownRuntime(runtime);
        }
    }
}
