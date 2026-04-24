using MDKOSS.Core;
using MDKOSS.Gui;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        var useConsoleMode = args.Any(a => string.Equals(a, "--console", StringComparison.OrdinalIgnoreCase));
        if (useConsoleMode)
        {
            await RunConsoleRuntimeAsync().ConfigureAwait(false);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static async Task RunConsoleRuntimeAsync()
    {
        var settingPath = Path.Combine(AppContext.BaseDirectory, "sample.setting.json");
        if (!File.Exists(settingPath))
        {
            Console.WriteLine($"Missing setting file: {settingPath}");
            return;
        }

        using var runtime = MdkRuntime.CreateFromFile(settingPath);
        runtime.Initialize();
        runtime.Start();

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
                // Normal shutdown path for Ctrl+C.
            }
        }
        else
        {
            Console.WriteLine("Press ENTER to stop...");
            Console.ReadLine();
        }

        await runtime.StopAsync().ConfigureAwait(false);
    }
}
