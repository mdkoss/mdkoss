using System.IO;
using System.Windows;
using MDKOSS.Core;
using MDKOSS.Extensions;
using MDKOSS.Host;
using MDKOSS.UI.WPF.Infrastructure;
using MDKOSS.UI.WPF.Services;
using MDKOSS.UI.WPF.ViewModels;
using MDKOSS.UI.WPF.ViewModels.Dialogs;
using MDKOSS.UI.WPF.ViewModels.Tools;
using MDKOSS.UI.WPF.ViewModels.Tools.Debug;
using MDKOSS.UI.WPF.ViewModels.Tools.Man;
using MDKOSS.UI.WPF.ViewModels.Tools.Monitor;
using MDKOSS.UI.WPF.Views;
using MDKOSS.UI.WPF.Views.Dialogs;
using MDKOSS.UI.WPF.Views.Tools;
using DebugViews = MDKOSS.UI.WPF.Views.Tools.Debug;
using ManViews = MDKOSS.UI.WPF.Views.Tools.Man;
using MonitorViews = MDKOSS.UI.WPF.Views.Tools.Monitor;
using Prism.DryIoc;
using Prism.Ioc;
using Prism.Navigation.Regions;

namespace MDKOSS.UI.WPF;

public partial class App : PrismApplication
{
    private const string AppTitle = "MDKOSS.UI.WPF";
    private string[] _args = [];
    private MdkRuntime? _runtime;

    protected override void OnStartup(StartupEventArgs e)
    {
        _args = e.Args;
        AppLog.Configure();
        MdkExtensionHost.DiscoverAndRegister(new ExtensionDiscoveryOptions
        {
            Log = msg => AppLog.Info(msg),
        });

        if (!TryCreateRuntime(out var error))
        {
            MessageBox.Show(error ?? "启动失败。", AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        base.OnStartup(e);
    }

    protected override Window CreateShell() => Container.Resolve<ShellView>();

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        if (_runtime is null)
        {
            return;
        }

        containerRegistry.RegisterInstance(_runtime);
        containerRegistry.RegisterSingleton<IRuntimeUiService, RuntimeUiService>();
        containerRegistry.RegisterSingleton<IToolNavigator, ToolNavigator>();

        containerRegistry.RegisterForNavigation<HomeView, HomeViewModel>(ViewNames.Home);
        containerRegistry.RegisterForNavigation<ToolHostView, ToolHostViewModel>(ViewNames.ToolHost);

        containerRegistry.RegisterForNavigation<MonitorViews.MonitorRuntimeView, MonitorRuntimeViewModel>("monitor_runtime");
        containerRegistry.RegisterForNavigation<MonitorViews.MonitorIoView, MonitorIoViewModel>("monitor_io");
        containerRegistry.RegisterForNavigation<MonitorViews.MonitorPlatformView, MonitorPlatformViewModel>("monitor_platform");
        containerRegistry.RegisterForNavigation<MonitorViews.MonitorAxisView, MonitorAxisViewModel>("monitor_axis");
        containerRegistry.RegisterForNavigation<MonitorViews.MonitorCameraView, MonitorCameraViewModel>("monitor_camera");
        containerRegistry.RegisterForNavigation<MonitorViews.MonitorVisionView, MonitorVisionViewModel>("monitor_vision");
        containerRegistry.RegisterForNavigation<MonitorViews.MonitorTaskView, MonitorTaskViewModel>("monitor_task");
        containerRegistry.RegisterForNavigation<MonitorViews.MonitorAlarmView, MonitorAlarmViewModel>("monitor_alarm");

        containerRegistry.RegisterForNavigation<DebugViews.DebugPlatformView, DebugPlatformViewModel>("debug_platform");
        containerRegistry.RegisterForNavigation<DebugViews.DebugSerialView, DebugSerialViewModel>("debug_serial");
        containerRegistry.RegisterForNavigation<DebugViews.DebugMysqlView, DebugMysqlViewModel>("debug_mysql");
        containerRegistry.RegisterForNavigation<DebugViews.DebugAxisView, DebugAxisViewModel>("debug_axis");
        containerRegistry.RegisterForNavigation<DebugViews.DebugIoView, DebugIoViewModel>("debug_io");
        containerRegistry.RegisterForNavigation<DebugViews.DebugCameraView, DebugCameraViewModel>("debug_camera");
        containerRegistry.RegisterForNavigation<DebugViews.DebugVisionView, DebugVisionViewModel>("debug_vision");
        containerRegistry.RegisterForNavigation<DebugViews.DebugDriverView, DebugDriverViewModel>("debug_driver");
        containerRegistry.RegisterForNavigation<DebugViews.DebugDbView, DebugDbViewModel>("debug_db");
        containerRegistry.RegisterForNavigation<DebugViews.DebugMachineView, DebugMachineViewModel>("debug_machine");
        containerRegistry.RegisterForNavigation<DebugViews.DebugAlarmView, DebugAlarmViewModel>("debug_alarm");

        containerRegistry.RegisterForNavigation<ManViews.ManMachineView, ManMachineViewModel>("man_machine");
        containerRegistry.RegisterForNavigation<ManViews.ManDriverView, ManDriverViewModel>("man_driver");
        containerRegistry.RegisterForNavigation<ManViews.ManDeviceView, ManDeviceViewModel>("man_device");
        containerRegistry.RegisterForNavigation<ManViews.ManAxisView, ManAxisViewModel>("man_axis");
        containerRegistry.RegisterForNavigation<ManViews.ManPlatformView, ManPlatformViewModel>("man_platform");
        containerRegistry.RegisterForNavigation<ManViews.ManGpioView, ManGpioViewModel>("man_gpio");
        containerRegistry.RegisterForNavigation<ManViews.ManTaskView, ManTaskViewModel>("man_task");
        containerRegistry.RegisterForNavigation<ManViews.ManVarsView, ManVarsViewModel>("man_vars");
        containerRegistry.RegisterForNavigation<ManViews.ManRecipeView, ManRecipeViewModel>("man_recipe");
        containerRegistry.RegisterForNavigation<ManViews.ManVisionView, ManVisionViewModel>("man_vision");
        containerRegistry.RegisterForNavigation<ManViews.ManAlarmView, ManAlarmViewModel>("man_alarm");

        containerRegistry.RegisterDialog<DevicesDialog, DevicesDialogViewModel>(DialogNames.Devices);
        containerRegistry.RegisterDialog<TasksDialog, TasksDialogViewModel>(DialogNames.Tasks);
        containerRegistry.RegisterDialog<VarsDialog, VarsDialogViewModel>(DialogNames.Vars);
        containerRegistry.RegisterDialog<AlarmsDialog, AlarmsDialogViewModel>(DialogNames.Alarms);
        containerRegistry.RegisterDialog<OrderDialog, OrderDialogViewModel>(DialogNames.Order);
        containerRegistry.RegisterDialog<RecipeDialog, RecipeDialogViewModel>(DialogNames.Recipe);
        containerRegistry.RegisterDialog<UserDialog, UserDialogViewModel>(DialogNames.User);
        containerRegistry.RegisterDialog<AboutDialog, AboutDialogViewModel>(DialogNames.About);
        containerRegistry.RegisterDialogWindow<NavyDialogWindow>();
    }

    protected override void OnInitialized()
    {
        var regions = Container.Resolve<IRegionManager>();
        regions.RegisterViewWithRegion(RegionNames.Content, typeof(HomeView));
        base.OnInitialized();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (Container.Resolve<IRuntimeUiService>() is IDisposable ui)
            {
                ui.Dispose();
            }
        }
        catch
        {
            // Container may already be torn down.
        }

        if (_runtime is not null)
        {
            RuntimeHost.ShutdownRuntime(_runtime);
            _runtime.Dispose();
            _runtime = null;
        }

        AppLog.Shutdown();
        base.OnExit(e);
    }

    private bool TryCreateRuntime(out string? error)
    {
        error = null;
        var settingPath = RuntimeHost.ResolveSettingPath(_args);
        AppLog.Info($"MDKOSS.UI.WPF starting (version: {MdkProduct.Version})");

        if (!File.Exists(settingPath))
        {
            error = $"找不到配置文件:\n{settingPath}\n\n请将 JSON 放到 exe 旁的 configs/，或使用 --setting 指定。";
            AppLog.Error($"Setting file not found: {settingPath}");
            return false;
        }

        if (!RuntimeHost.TryLoadSettings(settingPath, out var setting))
        {
            error = "加载配置失败，详见 logs。";
            return false;
        }

        try
        {
            _runtime = new MdkRuntime(setting, settingPath);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Failed to create runtime");
            error = ex.Message;
            return false;
        }

        if (!RuntimeHost.TryBootstrapRuntime(_runtime, out var startupError))
        {
            error = startupError ?? "Runtime 启动失败。";
            _runtime.Dispose();
            _runtime = null;
            return false;
        }

        return true;
    }
}
