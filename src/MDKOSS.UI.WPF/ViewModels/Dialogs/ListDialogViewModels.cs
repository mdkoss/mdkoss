using System.Collections.ObjectModel;
using System.Windows;
using MDKOSS.Core;
using MDKOSS.UI.WPF.Infrastructure;
using MDKOSS.UI.WPF.Models;
using MDKOSS.UI.WPF.Services;
using Prism.Commands;
using Prism.Dialogs;

namespace MDKOSS.UI.WPF.ViewModels.Dialogs;

public abstract class LiveDialogViewModel : DialogViewModelBase, IDisposable
{
    protected LiveDialogViewModel(IRuntimeUiService runtime)
    {
        Runtime = runtime;
        Runtime.SnapshotChanged += OnChanged;
    }

    protected IRuntimeUiService Runtime { get; }

    public override void OnDialogOpened(IDialogParameters parameters)
    {
        if (parameters.TryGetValue<string>("title", out var title) && !string.IsNullOrWhiteSpace(title))
        {
            Title = title;
        }

        Reload();
    }

    protected abstract void Reload();

    private void OnChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.Invoke(Reload);

    public override void OnDialogClosed() => Dispose();

    public void Dispose() => Runtime.SnapshotChanged -= OnChanged;
}

public sealed class DevicesDialogViewModel : LiveDialogViewModel
{
    public DevicesDialogViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        Title = "设备组件";
    }

    public ObservableCollection<DeviceRow> Items { get; } = [];

    protected override void Reload()
    {
        Items.Clear();
        var snap = Runtime.LatestSnapshot;
        if (snap is null)
        {
            return;
        }

        foreach (var d in snap.Devices.Values.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
        {
            Items.Add(new DeviceRow
            {
                Id = d.Id,
                Name = d.Name,
                Type = d.Type,
                State = d.State,
                Driver = d.DriverType,
                Online = d.DriverConnected ? "在线" : "离线",
            });
        }
    }
}

public sealed class TasksDialogViewModel : LiveDialogViewModel
{
    public TasksDialogViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        Title = "任务状态";
    }

    public ObservableCollection<TaskRow> Items { get; } = [];

    protected override void Reload()
    {
        Items.Clear();
        foreach (var t in Runtime.ListTasks())
        {
            Items.Add(new TaskRow
            {
                Name = t.Name,
                Type = t.Type,
                IntervalMs = t.IntervalMs,
                State = t.State,
            });
        }
    }
}

public sealed class VarsDialogViewModel : LiveDialogViewModel
{
    public VarsDialogViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        Title = "运行变量";
    }

    public ObservableCollection<VarRow> Items { get; } = [];

    protected override void Reload()
    {
        Items.Clear();
        foreach (var kv in SnapshotReader.Vars(Runtime.LatestSnapshot)
                     .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
                     .Take(80))
        {
            Items.Add(new VarRow { Key = kv.Key, Value = kv.Value?.ToString() ?? "" });
        }
    }
}

public sealed class AlarmsDialogViewModel : LiveDialogViewModel
{
    public AlarmsDialogViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        Title = "报警 / 异常";
        TriggerCommand = new DelegateCommand(() => Runtime.TryTriggerDemoAlarm(out _));
        ClearCommand = new DelegateCommand(() => Runtime.ClearAllAlarms());
    }

    public ObservableCollection<AlarmRow> Items { get; } = [];

    public DelegateCommand TriggerCommand { get; }

    public DelegateCommand ClearCommand { get; }

    protected override void Reload()
    {
        Items.Clear();
        foreach (var a in Runtime.ListActiveAlarms())
        {
            Items.Add(new AlarmRow
            {
                Id = a.EffectiveId,
                Code = a.Code,
                Name = a.Name,
                Level = a.Level,
                Message = a.EffectiveMessage,
                TriggerTime = a.TriggerTime,
            });
        }
    }
}

public sealed class OrderDialogViewModel : LiveDialogViewModel
{
    private string _orderId = "";

    public OrderDialogViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        Title = "工单详情";
    }

    public ObservableCollection<KvRow> Items { get; } = [];

    public override void OnDialogOpened(IDialogParameters parameters)
    {
        if (!parameters.TryGetValue<string>("orderId", out var orderId) || string.IsNullOrWhiteSpace(orderId))
        {
            orderId = Runtime.SelectedOrderId ?? "";
        }

        _orderId = orderId;

        base.OnDialogOpened(parameters);
    }

    protected override void Reload()
    {
        Items.Clear();
        var order = Runtime.ListOrders().FirstOrDefault(o =>
            string.Equals(o.Id, _orderId, StringComparison.OrdinalIgnoreCase));
        if (order is null)
        {
            Items.Add(new KvRow { Key = "提示", Value = "未找到工单" });
            return;
        }

        Items.Add(new KvRow { Key = "订单号", Value = order.Id });
        Items.Add(new KvRow { Key = "产品", Value = order.Product });
        Items.Add(new KvRow { Key = "数量", Value = order.Qty.ToString() });
        Items.Add(new KvRow { Key = "状态", Value = order.Status });
        Items.Add(new KvRow { Key = "进度", Value = $"{order.Progress:0}%" });
        Items.Add(new KvRow { Key = "配方", Value = order.RecipeId ?? "—" });
        Items.Add(new KvRow { Key = "优先级", Value = order.Priority.ToString() });
        Items.Add(new KvRow { Key = "备注", Value = order.Notes ?? "—" });
        Items.Add(new KvRow { Key = "更新", Value = SnapshotReader.FormatUtc(order.UpdatedAtUtc) });
        foreach (var kv in order.Fields)
        {
            Items.Add(new KvRow { Key = kv.Key, Value = kv.Value });
        }
    }
}

public sealed class RecipeDialogViewModel : LiveDialogViewModel
{
    private RecipeRow? _selected;

    public RecipeDialogViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        Title = "选择配方";
        ApplyCommand = new DelegateCommand(Apply, () => Selected is not null)
            .ObservesProperty(() => Selected);
    }

    public ObservableCollection<RecipeRow> Items { get; } = [];

    public RecipeRow? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public DelegateCommand ApplyCommand { get; }

    protected override void Reload()
    {
        var keep = Selected?.Id;
        var snap = Runtime.GetRecipeSnapshot();
        Items.Clear();
        foreach (var r in snap.Recipes)
        {
            Items.Add(new RecipeRow
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description ?? "",
                IsActive = string.Equals(r.Id, snap.ActiveRecipeId, StringComparison.OrdinalIgnoreCase),
            });
        }

        Selected = Items.FirstOrDefault(i => string.Equals(i.Id, keep, StringComparison.OrdinalIgnoreCase))
                   ?? Items.FirstOrDefault(i => i.IsActive)
                   ?? Items.FirstOrDefault();
    }

    private void Apply()
    {
        if (Selected is null)
        {
            return;
        }

        if (!Runtime.TryApplyRecipe(Selected.Id, out var error))
        {
            MessageBox.Show(error ?? "切换配方失败", "配方", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RequestClose.Invoke(ButtonResult.OK);
    }
}

public sealed class UserDialogViewModel : LiveDialogViewModel
{
    public UserDialogViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        Title = "当前用户";
    }

    public ObservableCollection<KvRow> Items { get; } = [];

    protected override void Reload()
    {
        var vars = SnapshotReader.Vars(Runtime.LatestSnapshot);
        Items.Clear();
        Items.Add(new KvRow { Key = "用户", Value = SnapshotReader.VarStr(vars, "user.name", "operator") });
        Items.Add(new KvRow { Key = "角色", Value = SnapshotReader.VarStr(vars, "user.role", "操作员") });
        Items.Add(new KvRow { Key = "说明", Value = "用户体系为占位，后续接入权限。" });
    }
}

public sealed class AboutDialogViewModel : LiveDialogViewModel
{
    private Action<string, string>? _navigate;

    public AboutDialogViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        Title = "关于 / 工具目录";
        OpenToolCommand = new DelegateCommand<ToolLinkRow>(OpenTool);
    }

    public ObservableCollection<KvRow> Info { get; } = [];

    public ObservableCollection<ToolLinkRow> MonitorLinks { get; } = [];

    public ObservableCollection<ToolLinkRow> DebugLinks { get; } = [];

    public ObservableCollection<ToolLinkRow> ManLinks { get; } = [];

    public DelegateCommand<ToolLinkRow> OpenToolCommand { get; }

    public override void OnDialogOpened(IDialogParameters parameters)
    {
        parameters.TryGetValue("navigate", out _navigate);
        foreach (var g in ToolCatalog.Groups)
        {
            var target = g.Id switch
            {
                "monitor" => MonitorLinks,
                "debug" => DebugLinks,
                _ => ManLinks,
            };
            foreach (var p in g.Pages)
            {
                target.Add(new ToolLinkRow { GroupId = g.Id, PageId = p.Id, Label = p.Label });
            }
        }

        base.OnDialogOpened(parameters);
    }

    protected override void Reload()
    {
        var snap = Runtime.LatestSnapshot;
        Info.Clear();
        Info.Add(new KvRow { Key = "项目", Value = snap?.ProjectName ?? Runtime.Runtime.Setting.ProjectName });
        Info.Add(new KvRow { Key = "版本", Value = snap?.Version ?? MdkProduct.Version });
        Info.Add(new KvRow { Key = "Release", Value = MdkProduct.ReleaseTag });
        Info.Add(new KvRow { Key = "运行时", Value = snap?.IsRunning == true ? "运行中" : "已停止" });
        Info.Add(new KvRow { Key = "监控", Value = Runtime.Runtime.MonitoringPrefix });
    }

    private void OpenTool(ToolLinkRow? link)
    {
        if (link is null)
        {
            return;
        }

        RequestClose.Invoke(ButtonResult.OK);
        _navigate?.Invoke(link.GroupId, link.PageId);
    }
}
