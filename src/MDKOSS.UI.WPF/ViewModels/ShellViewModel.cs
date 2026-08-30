using System.Collections.ObjectModel;
using System.Windows;
using MDKOSS.UI.WPF.Infrastructure;
using MDKOSS.UI.WPF.Models;
using MDKOSS.UI.WPF.Services;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using Prism.Navigation.Regions;

namespace MDKOSS.UI.WPF.ViewModels;

public sealed class ShellViewModel : BindableBase, IDisposable
{
    private readonly IRuntimeUiService _runtime;
    private readonly IDialogService _dialogs;
    private readonly IRegionManager _regions;
    private readonly IToolNavigator _navigator;
    private string _deviceName = "MDKOSS";
    private string _lampColor = "red";
    private string _currentOrderId = "—";
    private string _currentOrderDetail = "—";
    private string _recipeName = "未选择";
    private string _recipeId = "—";
    private string _activeNav = "";
    private string _contentMode = "home";
    private bool _canStart = true;
    private bool _canStop;
    private string _title = "MDKOSS";

    public ShellViewModel(IRuntimeUiService runtime, IDialogService dialogs, IRegionManager regions, IToolNavigator navigator)
    {
        _runtime = runtime;
        _dialogs = dialogs;
        _regions = regions;
        _navigator = navigator;
        _navigator.HomeRequested += (_, _) => GoHome();
        _navigator.ToolRequested += (_, _) =>
        {
            ContentMode = "tool";
            ActiveNav = "";
        };

        OpenDevicesCommand = new DelegateCommand(() => OpenDialog(DialogNames.Devices, "设备组件"));
        OpenTasksCommand = new DelegateCommand(() => OpenDialog(DialogNames.Tasks, "任务状态"));
        OpenVarsCommand = new DelegateCommand(() => OpenDialog(DialogNames.Vars, "运行变量"));
        OpenAlarmsCommand = new DelegateCommand(() => OpenDialog(DialogNames.Alarms, "报警 / 异常"));
        OpenOrderCommand = new DelegateCommand(() => OpenOrderDialog());
        OpenUserCommand = new DelegateCommand(() => OpenDialog(DialogNames.User, "当前用户"));
        OpenAboutCommand = new DelegateCommand(() => OpenAbout());
        PickRecipeCommand = new DelegateCommand(() => OpenDialog(DialogNames.Recipe, "选择配方"));
        GoHomeCommand = new DelegateCommand(GoHome);
        StartCommand = new DelegateCommand(() => _runtime.SendMachineCommand("start"), () => CanStart)
            .ObservesProperty(() => CanStart);
        StopCommand = new DelegateCommand(() => _runtime.SendMachineCommand("stop"), () => CanStop)
            .ObservesProperty(() => CanStop);
        ResetCommand = new DelegateCommand(() => _runtime.SendMachineCommand("reset"));

        _runtime.SnapshotChanged += OnSnapshotChanged;
        ApplySnapshot();
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string DeviceName
    {
        get => _deviceName;
        private set => SetProperty(ref _deviceName, value);
    }

    public string LampColor
    {
        get => _lampColor;
        private set => SetProperty(ref _lampColor, value);
    }

    public string CurrentOrderId
    {
        get => _currentOrderId;
        private set => SetProperty(ref _currentOrderId, value);
    }

    public string CurrentOrderDetail
    {
        get => _currentOrderDetail;
        private set => SetProperty(ref _currentOrderDetail, value);
    }

    public string RecipeName
    {
        get => _recipeName;
        private set => SetProperty(ref _recipeName, value);
    }

    public string RecipeIdText
    {
        get => _recipeId;
        private set => SetProperty(ref _recipeId, value);
    }

    public string ActiveNav
    {
        get => _activeNav;
        private set => SetProperty(ref _activeNav, value);
    }

    public string ContentMode
    {
        get => _contentMode;
        private set => SetProperty(ref _contentMode, value);
    }

    public bool CanStart
    {
        get => _canStart;
        private set => SetProperty(ref _canStart, value);
    }

    public bool CanStop
    {
        get => _canStop;
        private set => SetProperty(ref _canStop, value);
    }

    public ObservableCollection<StatusChipRow> StatusChips { get; } = [];

    public DelegateCommand OpenDevicesCommand { get; }
    public DelegateCommand OpenTasksCommand { get; }
    public DelegateCommand OpenVarsCommand { get; }
    public DelegateCommand OpenAlarmsCommand { get; }
    public DelegateCommand OpenOrderCommand { get; }
    public DelegateCommand OpenUserCommand { get; }
    public DelegateCommand OpenAboutCommand { get; }
    public DelegateCommand PickRecipeCommand { get; }
    public DelegateCommand GoHomeCommand { get; }
    public DelegateCommand StartCommand { get; }
    public DelegateCommand StopCommand { get; }
    public DelegateCommand ResetCommand { get; }

    public void NavigateToTool(string groupId, string? pageId = null)
    {
        _navigator.Navigate(groupId, pageId);
    }

    private void GoHome()
    {
        ContentMode = "home";
        ActiveNav = "";
        _regions.RequestNavigate(RegionNames.Content, ViewNames.Home);
    }

    private void OpenDialog(string name, string title, IDialogParameters? extra = null)
    {
        ActiveNav = name;
        extra ??= new DialogParameters();
        extra.Add("title", title);
        _dialogs.ShowDialog(name, extra, _ =>
        {
            ActiveNav = "";
            _runtime.Refresh();
        });
    }

    private void OpenOrderDialog()
    {
        var p = new DialogParameters
        {
            { "orderId", _runtime.SelectedOrderId ?? string.Empty },
        };
        OpenDialog(DialogNames.Order, "工单详情", p);
    }

    private void OpenAbout()
    {
        var p = new DialogParameters();
        p.Add("navigate", new Action<string, string>((group, page) =>
        {
            Application.Current.Dispatcher.BeginInvoke(() => NavigateToTool(group, page));
        }));
        OpenDialog(DialogNames.About, "关于 / 工具目录", p);
    }

    private void OnSnapshotChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.Invoke(ApplySnapshot);

    private void ApplySnapshot()
    {
        var snap = _runtime.LatestSnapshot;
        var vars = SnapshotReader.Vars(snap);
        var project = snap?.ProjectName ?? _runtime.Runtime.Setting.ProjectName;
        DeviceName = SnapshotReader.VarStr(vars, "machine.name", project);
        Title = $"MDKOSS - {project}";
        LampColor = SnapshotReader.VarStr(vars, "task.operation.lamp", "red").ToLowerInvariant();

        var opState = SnapshotReader.VarStr(vars, "task.operation.state", "—");
        var opRun = SnapshotReader.VarTruthy(vars, "task.operation.running");
        CanStart = !string.Equals(opState, "running", StringComparison.OrdinalIgnoreCase);
        CanStop = !string.Equals(opState, "stopped", StringComparison.OrdinalIgnoreCase);

        var ioOn = SnapshotReader.VarNum(vars, "task.cycle.io.online");
        var ioTot = SnapshotReader.VarNum(vars, "task.cycle.io.total");
        var ioOff = SnapshotReader.VarNum(vars, "task.cycle.io.offline", Math.Max(0, ioTot - ioOn));
        var devFault = SnapshotReader.VarNum(vars, "task.cycle.dev.fault");
        var devRun = SnapshotReader.VarNum(vars, "task.cycle.dev.running");

        StatusChips.Clear();
        StatusChips.Add(new StatusChipRow
        {
            Mode = snap?.IsRunning == true ? "ok" : "warn",
            Label = snap?.IsRunning == true ? "运行时 运行中" : "运行时 已停止",
        });
        StatusChips.Add(new StatusChipRow
        {
            Mode = ioOff > 0 ? "warn" : "ok",
            Label = $"驱动 {ioOn:0}/{ioTot:0} 在线",
        });
        StatusChips.Add(new StatusChipRow
        {
            Mode = devFault > 0 ? "bad" : "ok",
            Label = $"设备 运行 {devRun:0} · 故障 {devFault:0}",
        });
        StatusChips.Add(new StatusChipRow
        {
            Mode = opState == "fault" ? "bad" : opRun ? "ok" : "warn",
            Label = $"操作 {opState}",
        });

        var recipeName = SnapshotReader.VarStr(vars, "recipe.activeName", "");
        var recipeId = SnapshotReader.VarStr(vars, "recipe.activeId", "");
        if (string.IsNullOrWhiteSpace(recipeName) && string.IsNullOrWhiteSpace(recipeId))
        {
            RecipeName = "未选择";
            RecipeIdText = "—";
        }
        else
        {
            RecipeName = string.IsNullOrWhiteSpace(recipeName) ? recipeId : recipeName;
            RecipeIdText = string.IsNullOrWhiteSpace(recipeId) ? "—" : $"ID: {recipeId}";
        }

        var orders = _runtime.ListOrders();
        var selected = orders.FirstOrDefault(o =>
                           string.Equals(o.Id, _runtime.SelectedOrderId, StringComparison.OrdinalIgnoreCase))
                       ?? orders.FirstOrDefault();
        if (selected is null)
        {
            CurrentOrderId = "—";
            CurrentOrderDetail = "—";
            return;
        }

        _runtime.SelectedOrderId = selected.Id;
        CurrentOrderId = selected.Id;
        CurrentOrderDetail = $"{selected.Product} · {selected.Qty} 件 · {selected.Progress:0}%";
    }

    public void Dispose() => _runtime.SnapshotChanged -= OnSnapshotChanged;
}
