using System.Collections.ObjectModel;
using MDKOSS.UI.WPF.Infrastructure;
using MDKOSS.UI.WPF.Models;
using MDKOSS.UI.WPF.Services;
using Prism.Commands;

namespace MDKOSS.UI.WPF.ViewModels.Tools.Debug;

public sealed class DebugMachineViewModel : LiveToolViewModel
{
    public DebugMachineViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        StartCommand = new DelegateCommand(() => Cmd("start", "启动", true));
        PauseCommand = new DelegateCommand(() => Cmd("pause", "暂停", false));
        StopCommand = new DelegateCommand(() => Cmd("stop", "停止", true));
        ResetCommand = new DelegateCommand(() => Cmd("reset", "复位", true));
    }

    public string Project { get; private set; } = "—";
    public string RuntimeOn { get; private set; } = "—";
    public string Machine { get; private set; } = "—";
    public string Op { get; private set; } = "—";
    public string Fault { get; private set; } = "—";
    public DelegateCommand StartCommand { get; }
    public DelegateCommand PauseCommand { get; }
    public DelegateCommand StopCommand { get; }
    public DelegateCommand ResetCommand { get; }

    protected override void Reload()
    {
        var snap = Runtime.LatestSnapshot;
        var vars = SnapshotReader.Vars(snap);
        Project = snap?.ProjectName ?? Runtime.Runtime.Setting.ProjectName;
        RuntimeOn = snap?.IsRunning == true ? "运行中" : "已停止";
        Machine = SnapshotReader.VarStr(vars, "machine.state");
        Op = SnapshotReader.VarStr(vars, "task.operation.state");
        Fault = SnapshotReader.VarStr(vars, "task.cycle.dev.fault", "0");
        RaisePropertyChanged(nameof(Project));
        RaisePropertyChanged(nameof(RuntimeOn));
        RaisePropertyChanged(nameof(Machine));
        RaisePropertyChanged(nameof(Op));
        RaisePropertyChanged(nameof(Fault));
    }

    private void Cmd(string command, string label, bool confirm)
    {
        if (confirm && !DeviceKind.ConfirmWrite($"确认执行「{label}」？"))
        {
            return;
        }

        Runtime.SendMachineCommand(command);
        Toast($"{label} 已下发");
    }
}

public sealed class DebugAxisViewModel : LiveToolViewModel
{
    private AxisRow? _selected;
    private double _velocity = 1;
    private double _target;

    public DebugAxisViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        EnableCommand = new DelegateCommand(() => Enable(true));
        DisableCommand = new DelegateCommand(() => Enable(false));
        StopCommand = new DelegateCommand(() =>
        {
            if (Selected is null)
            {
                return;
            }

            Runtime.TryAxisStop(Selected.Id, out var err);
            Toast(err ?? "已停止", err is null);
        });
        MoveCommand = new DelegateCommand(() =>
        {
            if (Selected is null || !DeviceKind.ConfirmWrite($"定位到 {Target}？"))
            {
                return;
            }

            Runtime.TryAxisMove(Selected.Id, Target, out var err);
            Toast(err ?? "定位已下发", err is null);
        });
        JogPosCommand = new DelegateCommand(() => Jog(1));
        JogNegCommand = new DelegateCommand(() => Jog(-1));
        JogStopCommand = new DelegateCommand(() =>
        {
            if (Selected is not null)
            {
                Runtime.TryAxisStop(Selected.Id, out _);
            }
        });
    }

    public ObservableCollection<AxisRow> Axes { get; } = [];
    public AxisRow? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public double Velocity
    {
        get => _velocity;
        set => SetProperty(ref _velocity, value);
    }

    public double Target
    {
        get => _target;
        set => SetProperty(ref _target, value);
    }

    public DelegateCommand EnableCommand { get; }
    public DelegateCommand DisableCommand { get; }
    public DelegateCommand StopCommand { get; }
    public DelegateCommand MoveCommand { get; }
    public DelegateCommand JogPosCommand { get; }
    public DelegateCommand JogNegCommand { get; }
    public DelegateCommand JogStopCommand { get; }

    protected override void Reload()
    {
        var keep = Selected?.Id ?? PreferredDeviceId;
        Axes.Clear();
        foreach (var d in Runtime.LatestSnapshot?.Devices.Values.Where(DeviceKind.IsAxis) ?? [])
        {
            var st = d.AxisStatus;
            Axes.Add(new AxisRow
            {
                Id = d.Id,
                Name = d.Name,
                Type = d.Type,
                Online = DeviceKind.OnlineText(d.DriverConnected),
                Enabled = DeviceKind.EnabledText(st?.ServoOn),
                Prf = DeviceKind.Fmt(st?.PrfPosition),
                Enc = DeviceKind.Fmt(st?.EncPosition),
                Vel = DeviceKind.Fmt(st?.Velocity),
                Flags = DeviceKind.AxisFlags(st),
                State = d.State,
                Driver = d.DriverType,
            });
        }

        Selected = Axes.FirstOrDefault(a => string.Equals(a.Id, keep, StringComparison.OrdinalIgnoreCase))
                   ?? Axes.FirstOrDefault();
    }

    private void Enable(bool on)
    {
        if (Selected is null || !DeviceKind.ConfirmWrite(on ? "确认使能？" : "确认去使能？"))
        {
            return;
        }

        Runtime.TryAxisEnable(Selected.Id, on, out var err);
        Toast(err ?? (on ? "已使能" : "已去使能"), err is null);
    }

    private void Jog(double dir)
    {
        if (Selected is null)
        {
            return;
        }

        Runtime.TryAxisJog(Selected.Id, dir, Velocity, out var err);
        Toast(err ?? "点动中", err is null);
    }
}

public sealed class DebugPlatformViewModel : LiveToolViewModel
{
    private PlatformRow? _selected;
    private double _velocity = 1;
    private double _step = 1;

    public DebugPlatformViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        EnableCommand = new DelegateCommand(() => SetMotion(true));
        DisableCommand = new DelegateCommand(() => SetMotion(false));
        StopCommand = new DelegateCommand(StopAll);
        JogCommand = new DelegateCommand<string>(letter =>
        {
            if (Selected is null || string.IsNullOrWhiteSpace(letter))
            {
                return;
            }

            var dir = letter.EndsWith('-') ? -1 : 1;
            var axis = letter.TrimEnd('+', '-');
            Runtime.TryPlatformAxisJog(Selected.Id, axis, dir, Velocity, out var err);
            Toast(err ?? $"Jog {letter}", err is null);
        });
        StepCommand = new DelegateCommand<string>(letter =>
        {
            if (Selected is null || string.IsNullOrWhiteSpace(letter)
                || !Runtime.LatestSnapshot!.Devices.TryGetValue(Selected.Id, out var d))
            {
                return;
            }

            var dir = letter.EndsWith('-') ? -1 : 1;
            var axis = letter.TrimEnd('+', '-');
            var cur = d.PlatformAxes?.FirstOrDefault(a =>
                string.Equals(a.AxisLetter, axis, StringComparison.OrdinalIgnoreCase));
            var pos = (cur?.Position ?? 0) + dir * Step;
            Runtime.TryPlatformAxisMove(Selected.Id, axis, pos, out var err);
            Toast(err ?? $"步进 {letter}", err is null);
        });
    }

    public ObservableCollection<PlatformRow> Platforms { get; } = [];
    public ObservableCollection<PlatformAxisRow> Axes { get; } = [];

    public PlatformRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                LoadAxes();
            }
        }
    }

    public double Velocity
    {
        get => _velocity;
        set => SetProperty(ref _velocity, value);
    }

    public double Step
    {
        get => _step;
        set => SetProperty(ref _step, value);
    }

    public DelegateCommand EnableCommand { get; }
    public DelegateCommand DisableCommand { get; }
    public DelegateCommand StopCommand { get; }
    public DelegateCommand<string> JogCommand { get; }
    public DelegateCommand<string> StepCommand { get; }

    protected override void Reload()
    {
        var keep = Selected?.Id ?? PreferredDeviceId;
        Platforms.Clear();
        foreach (var d in Runtime.LatestSnapshot?.Devices.Values.Where(DeviceKind.IsPlatform) ?? [])
        {
            Platforms.Add(new PlatformRow
            {
                Id = d.Id,
                Name = d.Name,
                Type = d.Type,
                Online = DeviceKind.OnlineText(d.DriverConnected),
                State = d.State,
                AxisCount = d.PlatformAxes?.Count ?? 0,
            });
        }

        Selected = Platforms.FirstOrDefault(p => string.Equals(p.Id, keep, StringComparison.OrdinalIgnoreCase))
                   ?? Platforms.FirstOrDefault();
        LoadAxes();
    }

    private void LoadAxes()
    {
        Axes.Clear();
        if (Selected is null || Runtime.LatestSnapshot is null
            || !Runtime.LatestSnapshot.Devices.TryGetValue(Selected.Id, out var d)
            || d.PlatformAxes is null)
        {
            return;
        }

        foreach (var a in d.PlatformAxes)
        {
            Axes.Add(new PlatformAxisRow
            {
                Letter = a.AxisLetter,
                AxisId = a.AxisDeviceId ?? "",
                Driver = a.DriverId,
                Online = DeviceKind.OnlineText(a.DriverOnline),
                Enabled = DeviceKind.EnabledText(a.MotionEnabled),
                Prf = DeviceKind.Fmt(a.Position),
                Enc = DeviceKind.Fmt(a.EncPosition),
                Vel = DeviceKind.Fmt(a.Velocity),
                Flags = DeviceKind.AxisFlags(a.AxisStatus),
            });
        }
    }

    private void SetMotion(bool on)
    {
        if (Selected is null || !DeviceKind.ConfirmWrite(on ? "确认平台使能？" : "确认平台去使能？"))
        {
            return;
        }

        Runtime.TryPlatformEnable(Selected.Id, on, out var err);
        Toast(err ?? (on ? "已使能" : "已去使能"), err is null);
    }

    private void StopAll()
    {
        if (Selected is null)
        {
            return;
        }

        foreach (var a in Axes)
        {
            if (!string.IsNullOrEmpty(a.AxisId))
            {
                Runtime.TryAxisStop(a.AxisId, out _);
            }
        }

        Toast("已停止");
    }
}

public sealed class DebugIoViewModel : LiveToolViewModel
{
    public DebugIoViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        ToggleCommand = new DelegateCommand<IoPointRow>(row =>
        {
            if (row is null || !row.IsOutput || !DeviceKind.ConfirmWrite($"强制 {row.DeviceId}.{row.Alias} = {!row.IsOn}？"))
            {
                return;
            }

            Runtime.TryWriteIo(row.DeviceId, row.Alias, !row.IsOn, out var err);
            Toast(err ?? "已写入", err is null);
        });
    }

    public ObservableCollection<IoPointRow> Inputs { get; } = [];
    public ObservableCollection<IoPointRow> Outputs { get; } = [];
    public DelegateCommand<IoPointRow> ToggleCommand { get; }

    protected override void Reload()
    {
        Inputs.Clear();
        Outputs.Clear();
        foreach (var d in Runtime.LatestSnapshot?.Devices.Values.Where(DeviceKind.IsGpio) ?? [])
        {
            foreach (var p in d.GpioIoPoints ?? [])
            {
                var row = new IoPointRow
                {
                    DeviceId = d.Id,
                    DeviceName = d.Name,
                    Alias = p.Alias,
                    Direction = p.Direction,
                    DriverId = p.DriverId,
                    Address = p.Address,
                    Online = DeviceKind.OnlineText(p.DriverOnline),
                    Value = p.Value ?? "—",
                    IsOn = p.Value is "1" or "true" or "True" or "on",
                    IsOutput = DeviceKind.IsIoOut(p.Direction),
                };
                (row.IsOutput ? Outputs : Inputs).Add(row);
            }
        }
    }
}

public sealed class DebugAlarmViewModel : LiveToolViewModel
{
    public DebugAlarmViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        AckAllCommand = new DelegateCommand(() =>
        {
            if (!DeviceKind.ConfirmWrite("确认全部确认？"))
            {
                return;
            }

            var n = Runtime.AckAllAlarms();
            Toast($"已确认 {n} 条");
        });
        ResetCommand = new DelegateCommand(() =>
        {
            if (!DeviceKind.ConfirmWrite("确认复位锁存？"))
            {
                return;
            }

            Runtime.ClearAllAlarms();
            Toast("已复位");
        });
        TestCommand = new DelegateCommand(() =>
        {
            if (!DeviceKind.ConfirmWrite("确认模拟触发？"))
            {
                return;
            }

            Runtime.TryTriggerDemoAlarm(out var err);
            Toast(err ?? "已触发", err is null);
        });
        TaskResetCommand = new DelegateCommand(() =>
        {
            if (!DeviceKind.ConfirmWrite("确认任务复位并复位报警？"))
            {
                return;
            }

            Runtime.SendMachineCommand("reset");
            Runtime.ClearAllAlarms();
            Toast("任务+报警已复位");
        });
        LampCommand = new DelegateCommand<string>(color =>
        {
            if (string.IsNullOrWhiteSpace(color))
            {
                return;
            }

            Runtime.Runtime.Vars.Set("task.operation.lamp", color);
            Toast($"灯色 {color}");
        });
        ClearOneCommand = new DelegateCommand<AlarmRow>(row =>
        {
            if (row is null)
            {
                return;
            }

            Runtime.TryClearAlarm(row.Id, out var err);
            Toast(err ?? $"已清除 {row.Id}", err is null);
        });
    }

    public string VActive { get; private set; } = "0";
    public string VOp { get; private set; } = "—";
    public ObservableCollection<AlarmRow> Items { get; } = [];
    public DelegateCommand AckAllCommand { get; }
    public DelegateCommand ResetCommand { get; }
    public DelegateCommand TestCommand { get; }
    public DelegateCommand TaskResetCommand { get; }
    public DelegateCommand<string> LampCommand { get; }
    public DelegateCommand<AlarmRow> ClearOneCommand { get; }

    protected override void Reload()
    {
        var vars = SnapshotReader.Vars(Runtime.LatestSnapshot);
        VActive = Runtime.ListActiveAlarms().Count.ToString();
        VOp = SnapshotReader.VarStr(vars, "task.operation.state");
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

        RaisePropertyChanged(nameof(VActive));
        RaisePropertyChanged(nameof(VOp));
    }
}

public sealed class DebugDriverViewModel : LiveToolViewModel
{
    private string? _driverId;
    private string _address = "do.gpo.bit.0";
    private string _writeValue = "1";

    public DebugDriverViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        ReadCommand = new DelegateCommand(() =>
        {
            if (string.IsNullOrWhiteSpace(DriverId))
            {
                return;
            }

            Runtime.TryReadDriver(DriverId, Address, out var value, out var err);
            LastRead = err ?? value?.ToString() ?? "null";
            Toast(err ?? "读成功", err is null);
            RaisePropertyChanged(nameof(LastRead));
        });
        WriteCommand = new DelegateCommand(() =>
        {
            if (string.IsNullOrWhiteSpace(DriverId) || !DeviceKind.ConfirmWrite($"写 {Address} = {WriteValue}？"))
            {
                return;
            }

            object parsed = WriteValue;
            if (bool.TryParse(WriteValue, out var b))
            {
                parsed = b;
            }
            else if (double.TryParse(WriteValue, out var n))
            {
                parsed = n;
            }

            Runtime.TryWriteDriver(DriverId, Address, parsed, out var err);
            Toast(err ?? "写成功", err is null);
        });
    }

    public ObservableCollection<KvRow> Drivers { get; } = [];
    public string? DriverId
    {
        get => _driverId;
        set => SetProperty(ref _driverId, value);
    }

    public string Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    public string WriteValue
    {
        get => _writeValue;
        set => SetProperty(ref _writeValue, value);
    }

    public string LastRead { get; private set; } = "—";
    public string KpiTotal { get; private set; } = "0";
    public string KpiOnline { get; private set; } = "0";
    public DelegateCommand ReadCommand { get; }
    public DelegateCommand WriteCommand { get; }

    protected override void Reload()
    {
        var snap = Runtime.LatestSnapshot;
        Drivers.Clear();
        if (snap is not null)
        {
            foreach (var kv in snap.Drivers)
            {
                Drivers.Add(new KvRow { Key = kv.Key, Value = $"{kv.Value.Type} · {(kv.Value.IsConnected ? "在线" : "离线")}" });
            }
        }

        DriverId ??= Drivers.FirstOrDefault()?.Key;
        KpiTotal = (snap?.Drivers.Count ?? 0).ToString();
        KpiOnline = (snap?.Drivers.Count(d => d.Value.IsConnected) ?? 0).ToString();
        RaisePropertyChanged(nameof(KpiTotal));
        RaisePropertyChanged(nameof(KpiOnline));
    }
}

public sealed class DebugSerialViewModel : LiveToolViewModel
{
    private CameraRow? _selected;
    private string _payload = "hello";

    public DebugSerialViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        OpenCommand = new DelegateCommand(() => Act("open"));
        CloseCommand = new DelegateCommand(() => Act("close", confirm: true));
        SendCommand = new DelegateCommand(() =>
            Act("send", new Dictionary<string, object?> { ["text"] = Payload }));
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
    public DelegateCommand OpenCommand { get; }
    public DelegateCommand CloseCommand { get; }
    public DelegateCommand SendCommand { get; }

    protected override void Reload()
    {
        var keep = Selected?.Id ?? PreferredDeviceId;
        Devices.Clear();
        foreach (var d in Runtime.LatestSnapshot?.Devices.Values.Where(DeviceKind.IsSerial) ?? [])
        {
            Devices.Add(new CameraRow
            {
                Id = d.Id,
                Name = d.Name,
                Type = d.Type,
                State = d.State,
                Online = DeviceKind.OnlineText(d.DriverConnected),
                Driver = d.SerialPortInfo is null
                    ? ""
                    : $"{d.SerialPortInfo.PortName} {d.SerialPortInfo.BaudRate} open={d.SerialPortInfo.IsOpen}",
            });
        }

        Selected = Devices.FirstOrDefault(d => string.Equals(d.Id, keep, StringComparison.OrdinalIgnoreCase))
                   ?? Devices.FirstOrDefault();
        Link = Selected?.Driver ?? "—";
        RaisePropertyChanged(nameof(Link));
    }

    private void Act(string action, Dictionary<string, object?>? p = null, bool confirm = false)
    {
        if (Selected is null || (confirm && !DeviceKind.ConfirmWrite($"确认 {action}？")))
        {
            return;
        }

        var r = Runtime.ExecuteAction(Selected.Id, action, p);
        Toast(r.Success ? $"{action} 成功" : r.Error ?? "失败", r.Success);
    }
}

public sealed class DebugMysqlViewModel : LiveToolViewModel
{
    private CameraRow? _selected;
    private string _sql = "SELECT 1";

    public DebugMysqlViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        ConnectCommand = new DelegateCommand(() => Act("connect"));
        DisconnectCommand = new DelegateCommand(() => Act("disconnect", confirm: true));
        QueryCommand = new DelegateCommand(() =>
            Act("query", new Dictionary<string, object?> { ["sql"] = Sql }));
    }

    public ObservableCollection<CameraRow> Devices { get; } = [];
    public CameraRow? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public string Sql
    {
        get => _sql;
        set => SetProperty(ref _sql, value);
    }

    public DelegateCommand ConnectCommand { get; }
    public DelegateCommand DisconnectCommand { get; }
    public DelegateCommand QueryCommand { get; }

    protected override void Reload()
    {
        var keep = Selected?.Id ?? PreferredDeviceId;
        Devices.Clear();
        foreach (var d in Runtime.LatestSnapshot?.Devices.Values.Where(DeviceKind.IsMysql) ?? [])
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
        Toast(r.Success ? $"{action} 成功 {r.Data}" : r.Error ?? "失败", r.Success);
    }
}

public sealed class DebugCameraViewModel : LiveToolViewModel
{
    private CameraRow? _selected;

    public DebugCameraViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        OpenCommand = new DelegateCommand(() => Act("open"));
        CloseCommand = new DelegateCommand(() => Act("close", true));
        CaptureCommand = new DelegateCommand(() => Act("capture"));
    }

    public ObservableCollection<CameraRow> Devices { get; } = [];
    public CameraRow? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public DelegateCommand OpenCommand { get; }
    public DelegateCommand CloseCommand { get; }
    public DelegateCommand CaptureCommand { get; }

    protected override void Reload()
    {
        var keep = Selected?.Id ?? PreferredDeviceId;
        Devices.Clear();
        foreach (var d in Runtime.LatestSnapshot?.Devices.Values.Where(DeviceKind.IsCamera) ?? [])
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
    }

    private void Act(string action, bool confirm = false)
    {
        if (Selected is null || (confirm && !DeviceKind.ConfirmWrite($"确认 {action}？")))
        {
            return;
        }

        var r = Runtime.ExecuteAction(Selected.Id, action);
        Toast(r.Success ? $"{action} 成功" : r.Error ?? "失败", r.Success);
    }
}

public sealed class DebugVisionViewModel : LiveToolViewModel
{
    private CameraRow? _selected;

    public DebugVisionViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        RunCommand = new DelegateCommand(() => Act("run", true));
        CaptureRunCommand = new DelegateCommand(() => Act("captureAndRun", true));
    }

    public ObservableCollection<CameraRow> Devices { get; } = [];
    public CameraRow? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public string Result { get; private set; } = "—";
    public DelegateCommand RunCommand { get; }
    public DelegateCommand CaptureRunCommand { get; }

    protected override void Reload()
    {
        var keep = Selected?.Id ?? PreferredDeviceId;
        Devices.Clear();
        foreach (var d in Runtime.LatestSnapshot?.Devices.Values.Where(DeviceKind.IsVision) ?? [])
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
        var vars = SnapshotReader.Vars(Runtime.LatestSnapshot);
        Result = $"ok={SnapshotReader.VarStr(vars, "vision.ok")} score={SnapshotReader.VarStr(vars, "vision.score")}";
        RaisePropertyChanged(nameof(Result));
    }

    private void Act(string action, bool confirm)
    {
        if (Selected is null || (confirm && !DeviceKind.ConfirmWrite($"确认 {action}？")))
        {
            return;
        }

        var r = Runtime.ExecuteAction(Selected.Id, action);
        Toast(r.Success ? $"{action} 成功" : r.Error ?? "失败", r.Success);
    }
}

public sealed class DebugDbViewModel : LiveToolViewModel
{
    public DebugDbViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public string DbPath { get; private set; } = "—";
    public string OrderCount { get; private set; } = "0";
    public string RecipeCount { get; private set; } = "0";
    public ObservableCollection<KvRow> Orders { get; } = [];

    protected override void Reload()
    {
        DbPath = Runtime.Runtime.DataStore.DatabasePath;
        var orders = Runtime.ListOrders();
        OrderCount = orders.Count.ToString();
        RecipeCount = Runtime.GetRecipeSnapshot().Recipes.Count.ToString();
        Orders.Clear();
        foreach (var o in orders)
        {
            Orders.Add(new KvRow { Key = o.Id, Value = $"{o.Product} · {o.Status} · {o.Progress:0}%" });
        }

        RaisePropertyChanged(nameof(DbPath));
        RaisePropertyChanged(nameof(OrderCount));
        RaisePropertyChanged(nameof(RecipeCount));
    }
}
