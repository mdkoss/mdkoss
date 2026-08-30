using System.Collections.ObjectModel;
using System.Windows;
using MDKOSS.Core;
using MDKOSS.UI.WPF.Infrastructure;
using MDKOSS.UI.WPF.Models;
using MDKOSS.UI.WPF.Services;
using Prism.Commands;
using Prism.Mvvm;
using Prism.Navigation.Regions;

namespace MDKOSS.UI.WPF.ViewModels.Tools;

public sealed class ToolPageViewModel : BindableBase, INavigationAware, IDisposable
{
    private readonly IRuntimeUiService _runtime;
    private string _pageId = "";
    private string _title = "";
    private string _subtitle = "";
    private string _emptyHint = "";
    private bool _showMachineActions;
    private bool _showAlarmActions;

    public ToolPageViewModel(IRuntimeUiService runtime)
    {
        _runtime = runtime;
        StartCommand = new DelegateCommand(() => _runtime.SendMachineCommand("start"));
        StopCommand = new DelegateCommand(() => _runtime.SendMachineCommand("stop"));
        ResetCommand = new DelegateCommand(() => _runtime.SendMachineCommand("reset"));
        TriggerAlarmCommand = new DelegateCommand(() => _runtime.TryTriggerDemoAlarm(out _));
        ClearAlarmsCommand = new DelegateCommand(() => _runtime.ClearAllAlarms());
        _runtime.SnapshotChanged += OnChanged;
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    public string EmptyHint
    {
        get => _emptyHint;
        private set => SetProperty(ref _emptyHint, value);
    }

    public bool ShowMachineActions
    {
        get => _showMachineActions;
        private set => SetProperty(ref _showMachineActions, value);
    }

    public bool ShowAlarmActions
    {
        get => _showAlarmActions;
        private set => SetProperty(ref _showAlarmActions, value);
    }

    public ObservableCollection<KvRow> Rows { get; } = [];

    public DelegateCommand StartCommand { get; }
    public DelegateCommand StopCommand { get; }
    public DelegateCommand ResetCommand { get; }
    public DelegateCommand TriggerAlarmCommand { get; }
    public DelegateCommand ClearAlarmsCommand { get; }

    public void OnNavigatedTo(NavigationContext navigationContext)
    {
        var group = ToolCatalog.ResolveGroup(navigationContext.Parameters.GetValue<string>("group"));
        var page = ToolCatalog.ResolvePage(group, navigationContext.Parameters.GetValue<string>("page"));
        _pageId = page.Id;
        Title = $"{group.Label} · {page.Label}";
        ShowMachineActions = page.Id is "debug_machine" or "man_machine";
        ShowAlarmActions = page.Id is "debug_alarm" or "monitor_alarm";
        Reload();
    }

    public bool IsNavigationTarget(NavigationContext navigationContext) => true;

    public void OnNavigatedFrom(NavigationContext navigationContext)
    {
    }

    private void OnChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.Invoke(Reload);

    private void Reload()
    {
        if (string.IsNullOrEmpty(_pageId))
        {
            return;
        }

        Rows.Clear();
        var snap = _runtime.LatestSnapshot;
        var vars = SnapshotReader.Vars(snap);

        switch (_pageId)
        {
            case "monitor_runtime":
            case "debug_driver":
            case "man_driver":
            case "man_device":
                Subtitle = "驱动 / 设备快照";
                AddDrivers(snap);
                AddDevices(snap);
                break;
            case "monitor_io":
            case "debug_io":
            case "man_gpio":
                Subtitle = "GPIO / VIO 点位";
                AddIo(snap);
                break;
            case "monitor_platform":
            case "debug_platform":
            case "man_platform":
                Subtitle = "平台与轴";
                AddWhere(snap, d => d.PlatformAxes is { Count: > 0 } ||
                                    d.Type.Contains("xy", StringComparison.OrdinalIgnoreCase),
                    FormatPlatform);
                break;
            case "monitor_axis":
            case "debug_axis":
            case "man_axis":
                Subtitle = "轴状态";
                AddWhere(snap, d => d.AxisStatus is not null ||
                                    d.Type is "linear" or "rotary" or "axis",
                    FormatAxis);
                break;
            case "monitor_camera":
            case "debug_camera":
                Subtitle = "相机设备";
                AddByTypes(snap, "cameradev", "extcamera");
                break;
            case "monitor_vision":
            case "debug_vision":
            case "man_vision":
                Subtitle = "视觉变量";
                AddVars(vars, k => k.StartsWith("vision.", StringComparison.OrdinalIgnoreCase));
                break;
            case "monitor_task":
            case "man_task":
                Subtitle = "任务快照";
                foreach (var t in _runtime.ListTasks())
                {
                    Rows.Add(new KvRow { Key = t.Name, Value = $"{t.Type} · {t.State} · {t.IntervalMs}ms" });
                }
                break;
            case "monitor_alarm":
            case "debug_alarm":
            case "man_alarm":
                Subtitle = "活动报警";
                foreach (var a in _runtime.ListActiveAlarms())
                {
                    Rows.Add(new KvRow
                    {
                        Key = a.EffectiveId,
                        Value = $"{a.Level} · {a.EffectiveMessage} · {a.TriggerTime}",
                    });
                }
                break;
            case "debug_serial":
                Subtitle = "串口设备";
                AddByType(snap, "serialdev", d => d.SerialPortInfo is null
                    ? d.State
                    : $"{d.SerialPortInfo.PortName} {d.SerialPortInfo.BaudRate} open={d.SerialPortInfo.IsOpen}");
                break;
            case "debug_mysql":
                Subtitle = "MySQL 设备";
                AddByType(snap, "mysqldev", d => $"{d.State} · {d.DriverType}");
                break;
            case "debug_db":
            case "man_machine":
                Subtitle = "运行时 / 整机";
                Rows.Add(new KvRow { Key = "项目", Value = snap?.ProjectName ?? "—" });
                Rows.Add(new KvRow { Key = "版本", Value = snap?.Version ?? MdkProduct.Version });
                Rows.Add(new KvRow { Key = "运行中", Value = snap?.IsRunning == true ? "是" : "否" });
                Rows.Add(new KvRow { Key = "周期 ms", Value = _runtime.Runtime.Setting.CycleMs.ToString() });
                Rows.Add(new KvRow { Key = "数据库", Value = _runtime.Runtime.DataStore.DatabasePath });
                Rows.Add(new KvRow { Key = "监控", Value = _runtime.Runtime.MonitoringPrefix });
                break;
            case "debug_machine":
                Subtitle = "整机启停复位（写入 machine.command）";
                Rows.Add(new KvRow { Key = "操作状态", Value = SnapshotReader.VarStr(vars, "task.operation.state") });
                Rows.Add(new KvRow { Key = "灯色", Value = SnapshotReader.VarStr(vars, "task.operation.lamp") });
                break;
            case "man_vars":
                Subtitle = "运行变量";
                AddVars(vars, _ => true);
                break;
            case "man_recipe":
                Subtitle = "配方";
                var rec = _runtime.GetRecipeSnapshot();
                foreach (var r in rec.Recipes)
                {
                    var mark = string.Equals(r.Id, rec.ActiveRecipeId, StringComparison.OrdinalIgnoreCase) ? " [当前]" : "";
                    Rows.Add(new KvRow { Key = r.Id, Value = $"{r.Name}{mark}  {r.Description}" });
                }
                break;
            default:
                Subtitle = "快照一览";
                AddDevices(snap);
                break;
        }

        EmptyHint = Rows.Count == 0 ? "暂无数据。完整写操作仍以 CEF 工具页 / Config.Wpf 为准。" : "";
    }

    private void AddDrivers(RuntimeSnapshot? snap)
    {
        if (snap is null)
        {
            return;
        }

        foreach (var kv in snap.Drivers.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            Rows.Add(new KvRow
            {
                Key = $"驱动 {kv.Key}",
                Value = $"{kv.Value.Type} · {(kv.Value.IsConnected ? "在线" : "离线")}",
            });
        }
    }

    private void AddDevices(RuntimeSnapshot? snap)
    {
        if (snap is null)
        {
            return;
        }

        foreach (var d in snap.Devices.Values.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
        {
            Rows.Add(new KvRow
            {
                Key = d.Name,
                Value = $"{d.Id} · {d.Type} · {d.State} · {(d.DriverConnected ? "驱动在线" : "驱动离线")}",
            });
        }
    }

    private void AddByType(RuntimeSnapshot? snap, string type, Func<DeviceSnapshot, string> format) =>
        AddWhere(snap, d => string.Equals(d.Type, type, StringComparison.OrdinalIgnoreCase), format);

    private void AddWhere(RuntimeSnapshot? snap, Func<DeviceSnapshot, bool> pred, Func<DeviceSnapshot, string> format)
    {
        if (snap is null)
        {
            return;
        }

        foreach (var d in snap.Devices.Values
                     .Where(pred)
                     .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
        {
            Rows.Add(new KvRow { Key = d.Name, Value = format(d) });
        }
    }

    private void AddByTypes(RuntimeSnapshot? snap, params string[] types)
    {
        if (snap is null)
        {
            return;
        }

        foreach (var d in snap.Devices.Values
                     .Where(d => types.Contains(d.Type, StringComparer.OrdinalIgnoreCase))
                     .OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
        {
            Rows.Add(new KvRow { Key = d.Name, Value = $"{d.Id} · {d.Type} · {d.State}" });
        }
    }

    private void AddIo(RuntimeSnapshot? snap)
    {
        if (snap is null)
        {
            return;
        }

        foreach (var d in snap.Devices.Values.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (d.GpioIoPoints is null)
            {
                continue;
            }

            foreach (var p in d.GpioIoPoints)
            {
                Rows.Add(new KvRow
                {
                    Key = $"{d.Name}.{p.Alias}",
                    Value = $"{p.Direction} {p.Address} = {p.Value ?? "—"} · {(p.DriverOnline ? "在线" : "离线")}",
                });
            }
        }
    }

    private void AddVars(IReadOnlyDictionary<string, object?> vars, Func<string, bool> pred)
    {
        foreach (var kv in vars.Where(kv => pred(kv.Key)).OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            Rows.Add(new KvRow { Key = kv.Key, Value = kv.Value?.ToString() ?? "" });
        }
    }

    private static string FormatPlatform(DeviceSnapshot d)
    {
        if (d.PlatformAxes is null || d.PlatformAxes.Count == 0)
        {
            return d.State;
        }

        var axes = string.Join("  ", d.PlatformAxes.Select(a =>
            $"{a.AxisLetter}:{a.Position?.ToString("0.###") ?? "—"}"));
        return $"{d.State}  {axes}";
    }

    private static string FormatAxis(DeviceSnapshot d)
    {
        var st = d.AxisStatus;
        if (st is null)
        {
            return d.State;
        }

        return $"{d.State}  prf={st.Value.PrfPosition:0.###}  enc={st.Value.EncPosition:0.###}  " +
               $"servo={(st.Value.ServoOn ? "on" : "off")}  moving={(st.Value.Moving ? "1" : "0")}";
    }

    public void Dispose() => _runtime.SnapshotChanged -= OnChanged;
}
