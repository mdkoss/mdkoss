using MDKOSS.Core;

namespace MDKOSS.Host;

/// <summary>
/// Shared setting resolution and runtime bootstrap for desktop / console hosts.
/// </summary>
public static class RuntimeHost
{
    public static string ResolveSettingPath(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--setting", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(args[i], "--settings", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
            {
                break;
            }

            var specified = args[i + 1].Trim().Trim('"');
            if (File.Exists(specified))
            {
                return Path.GetFullPath(specified);
            }

            var fromBase = Path.Combine(AppContext.BaseDirectory, specified);
            if (File.Exists(fromBase))
            {
                return Path.GetFullPath(fromBase);
            }

            return Path.GetFullPath(specified);
        }

        return ResolveDefaultSettingPath();
    }

    public static string ResolveDefaultSettingPath()
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

    public static bool IsPnpSettingPath(string settingPath)
    {
        var name = Path.GetFileName(settingPath);
        return name.Contains("pnp", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPnpProject(MdkSetting setting, string settingPath)
    {
        return IsPnpSettingPath(settingPath)
               || setting.ProjectName.Contains("PNP", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryLoadSettings(string settingPath, out MdkSetting setting)
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

    public static bool TryBootstrapRuntime(MdkRuntime runtime, out string? errorMessage)
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

    public static void ShutdownRuntime(MdkRuntime runtime)
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

    public static async Task RunConsoleRuntimeAsync(string settingPath)
    {
        AppLog.Configure();
        AppLog.Info("MDKOSS starting (console mode)");

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
        Console.WriteLine($"PNP home: {runtime.MonitoringPrefix}indexPnp.html");
        Console.WriteLine($"PNP cycle: {runtime.MonitoringPrefix}monitorPnp.html");

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
