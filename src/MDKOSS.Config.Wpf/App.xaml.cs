using System.Windows;
using MDKOSS.Core;

namespace MDKOSS.Config.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.Configure();

        var settingPath = ResolveSettingPath(e.Args);
        var main = new MainWindow(settingPath);
        main.Show();
    }

    private static string ResolveSettingPath(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--setting", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(args[i + 1]))
            {
                return System.IO.Path.GetFullPath(args[i + 1]);
            }
        }

        var defaultPath = System.IO.Path.Combine(AppContext.BaseDirectory, "configs", "sample.setting.json");
        return System.IO.File.Exists(defaultPath) ? defaultPath : MdkSetting.DefaultSettingsPath;
    }
}
