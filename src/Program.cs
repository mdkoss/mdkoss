using MDKOSS.Core;
using MDKOSS.Gui;
using MDKOSS.Gui.CefUi;
using System.Windows.Forms;

internal static class Program
{
    private enum UiMode
    {
        WinForms,
        Cef,
        Console
    }

    [STAThread]
    private static async Task Main(string[] args)
    {
        var uiMode = ParseUiMode(args);
        if (uiMode == UiMode.Console)
        {
            await RunConsoleRuntimeAsync().ConfigureAwait(false);
            return;
        }

        RunDesktopUi(uiMode);
    }

    private static UiMode ParseUiMode(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--console", StringComparison.OrdinalIgnoreCase)))
        {
            return UiMode.Console;
        }

        if (args.Any(a => string.Equals(a, "--cef", StringComparison.OrdinalIgnoreCase)))
        {
            return UiMode.Cef;
        }

        if (args.Any(a => string.Equals(a, "--winform", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(a, "--winforms", StringComparison.OrdinalIgnoreCase)))
        {
            return UiMode.WinForms;
        }

        return UiMode.WinForms;
    }

    private static void RunDesktopUi(UiMode uiMode)
    {
        AppLog.Configure();
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        var modeLabel = uiMode == UiMode.Cef ? "CEF" : "WinForms";
        AppLog.Info($"MDKOSS starting ({modeLabel} mode, version: {version})================================================");

        ApplicationConfiguration.Initialize();

        var settingPath = ResolveDefaultSettingPath();
        if (!File.Exists(settingPath))
        {
            AppLog.Error($"Setting file not found: {settingPath}");
            MessageBox.Show(
                $"Setting file not found:\n{settingPath}",
                "MDKOSS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (!TryLoadSettings(settingPath, out var setting))
        {
            MessageBox.Show(
                "Failed to load settings. See logs for details.",
                "Settings Load Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        using var runtime = new MdkRuntime(setting);
        if (!TryBootstrapRuntime(runtime, out var startupError))
        {
            MessageBox.Show(
                startupError ?? "Startup failed.",
                "Startup Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        if (uiMode == UiMode.Cef)
        {
            RunCefUi(runtime);
        }
        else
        {
            RunWinFormsUi(runtime, settingPath);
        }
    }

    private static void RunWinFormsUi(MdkRuntime runtime, string settingPath)
    {
        try
        {
            AppLog.Info("WinForms UI starting");
            Application.Run(new MainForm(runtime, settingPath));
            AppLog.Info("WinForms UI closed");
        }
        finally
        {
            ShutdownRuntime(runtime);
        }
    }

    private static void RunCefUi(MdkRuntime runtime)
    {
        if (!CefRuntimeBootstrap.TryInitialize(out var cefError))
        {
            AppLog.Error($"CEF init failed: {cefError}");
            MessageBox.Show(
                cefError ?? "Failed to initialize CEF.",
                "CEF Startup Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            ShutdownRuntime(runtime);
            return;
        }

        try
        {
            AppLog.Info("CEF UI starting (views/index.html)");
            Application.Run(new CefMainForm(runtime));
            AppLog.Info("CEF UI closed");
        }
        finally
        {
            CefRuntimeBootstrap.Shutdown();
            ShutdownRuntime(runtime);
        }
    }

    private static string ResolveDefaultSettingPath()
    {
        if (File.Exists(MdkSetting.DefaultSettingsPath))
        {
            return MdkSetting.DefaultSettingsPath;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "MDKOSS", "configs", "sample.setting.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return Path.Combine(Environment.CurrentDirectory, "MDKOSS", "configs", "sample.setting.json");
    }

    private static bool TryLoadSettings(string settingPath, out MdkSetting setting)
    {
        AppLog.Info($"Loading settings: {settingPath}");
        try
        {
            setting = MdkSetting.Load(settingPath);
            AppLog.Info(
                $"Settings loaded (project: {setting.ProjectName}, " +
                $"drivers: {setting.Drivers.Count}, devices: {setting.Devices.Count}, tasks: {setting.Tasks.Count})");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, $"Failed to load settings: {settingPath}");
            setting = new MdkSetting();
            return false;
        }
    }

    private static bool TryBootstrapRuntime(MdkRuntime runtime, out string? errorMessage)
    {
        try
        {
            AppLog.Info("Runtime init: Initialize()");
            runtime.Initialize();
            AppLog.Info("Runtime init: Start()");
            runtime.Start();
            AppLog.Info($"Runtime started (project: {runtime.Setting.ProjectName}, monitor: {runtime.MonitoringPrefix})");
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Runtime init failed");
            errorMessage = ex.Message;
            return false;
        }
    }

    private static void ShutdownRuntime(MdkRuntime runtime)
    {
        AppLog.Info("Runtime uninit: StopAsync()");
        try
        {
            runtime.StopAsync().GetAwaiter().GetResult();
            AppLog.Info("Runtime uninit: stopped");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Runtime uninit failed during StopAsync()");
        }
    }

    private static async Task RunConsoleRuntimeAsync()
    {
        AppLog.Configure();
        AppLog.Info("MDKOSS starting (console mode)");

        var settingPath = MdkSetting.DefaultSettingsPath;
        if (!File.Exists(settingPath))
        {
            AppLog.Error($"Setting file not found: {settingPath}");
            Console.WriteLine($"Missing setting file: {settingPath}");
            return;
        }

        if (!TryLoadSettings(settingPath, out var setting))
        {
            Console.WriteLine($"Failed to load settings: {settingPath}");
            return;
        }

        using var runtime = new MdkRuntime(setting);
        if (!TryBootstrapRuntime(runtime, out var startupError))
        {
            Console.WriteLine(startupError ?? "Startup failed.");
            return;
        }

        Console.WriteLine("MDKOSS runtime started.");
        Console.WriteLine($"Monitor UI: {runtime.MonitoringPrefix}");

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };

        if (Console.IsInputRedirected)
        {
            Console.WriteLine("Input redirected. Press Ctrl+C to stop...");
            try
            {
                await Task.Delay(Timeout.Infinite, shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                AppLog.Info("Console shutdown requested (Ctrl+C)");
            }
        }
        else
        {
            Console.WriteLine("Press ENTER to stop...");
            Console.ReadLine();
        }

        ShutdownRuntime(runtime);
        AppLog.Info("MDKOSS console mode exiting");
    }
}
