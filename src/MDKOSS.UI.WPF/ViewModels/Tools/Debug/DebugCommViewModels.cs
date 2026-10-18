using System.Collections.ObjectModel;
using System.Text.Json;
using MDKOSS.UI.WPF.Infrastructure;
using MDKOSS.UI.WPF.Models;
using MDKOSS.UI.WPF.Services;
using Prism.Commands;

namespace MDKOSS.UI.WPF.ViewModels.Tools.Debug;

public sealed class DebugTcpViewModel : LiveToolViewModel
{
    private CameraRow? _selected;
    private string _payload = "hello";

    public DebugTcpViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        ConnectCommand = new DelegateCommand(() => Act("connect"));
        DisconnectCommand = new DelegateCommand(() => Act("disconnect", confirm: true));
        WriteCommand = new DelegateCommand(() =>
            Act("write", new Dictionary<string, object?> { ["data"] = Payload }));
        ReadCommand = new DelegateCommand(() => Act("read"));
    }

    public ObservableCollection<CameraRow> Devices { get; } = [];

    public CameraRow? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public string Payload
    {
        get => _payload;
        set => SetProperty(ref _payload, value);
    }

    public string Link { get; private set; } = "—";
    public DelegateCommand ConnectCommand { get; }
    public DelegateCommand DisconnectCommand { get; }
    public DelegateCommand WriteCommand { get; }
    public DelegateCommand ReadCommand { get; }

    protected override void Reload()
    {
        var keep = Selected?.Id ?? PreferredDeviceId;
        Devices.Clear();
        foreach (var d in Runtime.LatestSnapshot?.Devices.Values.Where(DeviceKind.IsTcp) ?? [])
        {
            Devices.Add(new CameraRow
            {
                Id = d.Id,
                Name = d.Name,
                Type = d.Type,
                State = d.State,
                Online = DeviceKind.OnlineText(d.DriverConnected),
                Driver = d.DriverType,
            });
        }

        Selected = Devices.FirstOrDefault(d => string.Equals(d.Id, keep, StringComparison.OrdinalIgnoreCase))
                   ?? Devices.FirstOrDefault();
        Link = Selected is null ? "—" : $"{Selected.Name} · {Selected.Online}";
        RaisePropertyChanged(nameof(Link));
    }

    private void Act(string action, Dictionary<string, object?>? p = null, bool confirm = false)
    {
        if (Selected is null || (confirm && !DeviceKind.ConfirmWrite($"确认 {action}？")))
        {
            return;
        }

        var r = Runtime.ExecuteAction(Selected.Id, action, p);
        Toast(r.Success ? $"{action} 成功 {Format(r.Data)}" : r.Error ?? "失败", r.Success);
    }

    private static string Format(object? data) => data is null ? "" : JsonSerializer.Serialize(data);
}

public sealed class DebugPyScriptViewModel : LiveToolViewModel
{
    private CameraRow? _selected;
    private string _arguments = "demo 1";

    public DebugPyScriptViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        RunCommand = new DelegateCommand(() =>
            Act("run", new Dictionary<string, object?> { ["arguments"] = Arguments }));
        KillCommand = new DelegateCommand(() => Act("kill", confirm: true));
        StatusCommand = new DelegateCommand(() => Act("status"));
    }

    public ObservableCollection<CameraRow> Devices { get; } = [];

    public CameraRow? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public string Arguments
    {
        get => _arguments;
        set => SetProperty(ref _arguments, value);
    }

    public string Detail { get; private set; } = "—";
    public DelegateCommand RunCommand { get; }
    public DelegateCommand KillCommand { get; }
    public DelegateCommand StatusCommand { get; }

    protected override void Reload()
    {
        var keep = Selected?.Id ?? PreferredDeviceId;
        Devices.Clear();
        foreach (var d in Runtime.LatestSnapshot?.Devices.Values.Where(DeviceKind.IsPyScript) ?? [])
        {
            Devices.Add(new CameraRow
            {
                Id = d.Id,
                Name = d.Name,
                Type = d.Type,
                State = d.State,
                Online = DeviceKind.OnlineText(d.DriverConnected),
            });
        }

        Selected = Devices.FirstOrDefault(d => string.Equals(d.Id, keep, StringComparison.OrdinalIgnoreCase))
                   ?? Devices.FirstOrDefault();
    }

    private void Act(string action, Dictionary<string, object?>? p = null, bool confirm = false)
    {
        if (Selected is null || (confirm && !DeviceKind.ConfirmWrite($"确认 {action}？")))
        {
            return;
        }

        var r = Runtime.ExecuteAction(Selected.Id, action, p);
        Detail = r.Success ? JsonSerializer.Serialize(r.Data) : r.Error ?? "失败";
        RaisePropertyChanged(nameof(Detail));
        Toast(r.Success ? $"{action} 成功" : r.Error ?? "失败", r.Success);
    }
}

public sealed class DebugModbusViewModel : LiveToolViewModel
{
    private CameraRow? _selected;
    private string _address = "0";
    private string _count = "4";
    private string _values = "1,2,3,4";

    public DebugModbusViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        StartCommand = new DelegateCommand(() => Act(IsClient ? "connect" : "start"));
        StopCommand = new DelegateCommand(() => Act(IsClient ? "disconnect" : "stop", confirm: true));
        ReadCommand = new DelegateCommand(() =>
            Act("readholding", new Dictionary<string, object?>
            {
                ["address"] = ParseUShort(Address),
                ["count"] = ParseUShort(Count),
            }));
        WriteCommand = new DelegateCommand(() =>
        {
            if (!DeviceKind.ConfirmWrite("确认写入 Holding？"))
            {
                return;
            }

            Act("writeholding", new Dictionary<string, object?>
            {
                ["address"] = ParseUShort(Address),
                ["values"] = ParseValues(Values),
            });
        });
    }

    public ObservableCollection<CameraRow> Devices { get; } = [];

    public CameraRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                RaisePropertyChanged(nameof(IsClient));
                RaisePropertyChanged(nameof(Role));
            }
        }
    }

    public string Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    public string Count
    {
        get => _count;
        set => SetProperty(ref _count, value);
    }

    public string Values
    {
        get => _values;
        set => SetProperty(ref _values, value);
    }

    public bool IsClient => Selected?.Type is "devmodclient";
    public string Role => Selected is null ? "—" : IsClient ? "Client" : "Server";
    public string Detail { get; private set; } = "—";
    public DelegateCommand StartCommand { get; }
    public DelegateCommand StopCommand { get; }
    public DelegateCommand ReadCommand { get; }
    public DelegateCommand WriteCommand { get; }

    protected override void Reload()
    {
        var keep = Selected?.Id ?? PreferredDeviceId;
        Devices.Clear();
        foreach (var d in Runtime.LatestSnapshot?.Devices.Values.Where(DeviceKind.IsModbus) ?? [])
        {
            Devices.Add(new CameraRow
            {
                Id = d.Id,
                Name = d.Name,
                Type = d.Type,
                State = d.State,
                Online = DeviceKind.OnlineText(d.DriverConnected),
            });
        }

        Selected = Devices.FirstOrDefault(d => string.Equals(d.Id, keep, StringComparison.OrdinalIgnoreCase))
                   ?? Devices.FirstOrDefault();
    }

    private void Act(string action, Dictionary<string, object?>? p = null, bool confirm = false)
    {
        if (Selected is null || (confirm && !DeviceKind.ConfirmWrite($"确认 {action}？")))
        {
            return;
        }

        var r = Runtime.ExecuteAction(Selected.Id, action, p);
        Detail = r.Success ? JsonSerializer.Serialize(r.Data) : r.Error ?? "失败";
        RaisePropertyChanged(nameof(Detail));
        Toast(r.Success ? $"{action} 成功" : r.Error ?? "失败", r.Success);
    }

    private static ushort ParseUShort(string text) =>
        ushort.TryParse(text, out var n) ? n : (ushort)0;

    private static int[] ParseValues(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .ToArray();
}
