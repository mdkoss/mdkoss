using System.Windows;
using MDKOSS.Core;
using MDKOSS.Extensions;
using MDKOSS.Host;
using MDKOSS.Tools.Calib.Calib;
using MDKOSS.Tools.Calib.ViewModels;
using MDKOSS.Tools.Calib.Views;

namespace MDKOSS.Tools.Calib;

public partial class App : Application
{
    private MainViewModel? _vm;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.Configure();
        MdkExtensionHost.DiscoverAndRegister(new ExtensionDiscoveryOptions
        {
            Log = msg => AppLog.Info(msg),
        });
        CalibExtensionBootstrap.Register();

        _vm = new MainViewModel();
        var path = RuntimeHost.ResolveSettingPath(e.Args);
        if (!_vm.TryLoad(path, out var error))
        {
            MessageBox.Show(
                error ?? "启动失败。",
                "MDKOSS.Tools.Calib",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        var main = new MainWindow(_vm);
        main.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _vm?.Dispose();
        AppLog.Shutdown();
        base.OnExit(e);
    }
}
