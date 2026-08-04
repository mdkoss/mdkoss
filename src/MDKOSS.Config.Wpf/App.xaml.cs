using System.Windows;
using MDKOSS.Core;

namespace MDKOSS.Config.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.Configure();

        var path = ResolveDocumentPath(e.Args);
        var main = new MainWindow(path);
        main.Show();
    }

    private static string ResolveDocumentPath(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if ((string.Equals(args[i], "--setting", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(args[i], "--db", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(args[i], "--file", StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                return System.IO.Path.GetFullPath(args[i + 1]);
            }
        }

        // Bare path argument
        foreach (var arg in args)
        {
            if (arg.StartsWith('-'))
            {
                continue;
            }

            if (System.IO.File.Exists(arg))
            {
                return System.IO.Path.GetFullPath(arg);
            }
        }

        var defaultPath = System.IO.Path.Combine(AppContext.BaseDirectory, "configs", "sample.setting.json");
        return System.IO.File.Exists(defaultPath) ? defaultPath : MdkSetting.DefaultSettingsPath;
    }
}
