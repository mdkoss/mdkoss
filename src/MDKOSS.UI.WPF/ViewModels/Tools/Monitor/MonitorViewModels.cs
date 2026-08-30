using System.Collections.ObjectModel;
using MDKOSS.Core;
using MDKOSS.UI.WPF.Infrastructure;
using MDKOSS.UI.WPF.Models;
using MDKOSS.UI.WPF.Services;
using Prism.Commands;

namespace MDKOSS.UI.WPF.ViewModels.Tools.Monitor;

public sealed class MonitorRuntimeViewModel : LiveToolViewModel
{
    public MonitorRuntimeViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public string ProjectName { get; private set; } = "—";
    public string RuntimeStatus { get; private set; } = "—";
    public string DriverOnline { get; private set; } = "0";
    public string DeviceOnline { get; private set; } = "0";
    public string LastUpdate { get; private set; } = "—";
    public string OpState { get; private set; } = "—";
    public string OpMsg { get; private set; } = "—";
    public string OpLamp { get; private set; } = "—";
    public string Recipe { get; private set; } = "—";
    public ObservableCollection<MatrixTile> Drivers { get; } = [];
    public ObservableCollection<MatrixTile> Devices { get; } = [];

    protected override void Reload()
    {
        var snap = Runtime.LatestSnapshot;
        var vars = SnapshotReader.Vars(snap);
        ProjectName = snap?.ProjectName ?? Runtime.Runtime.Setting.ProjectName;
        RuntimeStatus = snap?.IsRunning == true ? "运行中" : "已停止";
        DriverOnline = $"{snap?.Drivers.Count(d => d.Value.IsConnected) ?? 0}/{snap?.Drivers.Count ?? 0}";
        DeviceOnline = $"{snap?.Devices.Count(d => d.Value.DriverConnected) ?? 0}/{snap?.Devices.Count ?? 0}";
        LastUpdate = DateTime.Now.ToString("HH:mm:ss");
        OpState = SnapshotReader.VarStr(vars, "task.operation.state");
        OpMsg = SnapshotReader.VarStr(vars, "task.operation.message", SnapshotReader.VarStr(vars, "task.operation.msg"));
        OpLamp = SnapshotReader.VarStr(vars, "task.operation.lamp");
        Recipe = SnapshotReader.VarStr(vars, "recipe.activeName", SnapshotReader.VarStr(vars, "recipe.activeId"));

        Drivers.Clear();
        if (snap is not null)
        {
            foreach (var kv in snap.Drivers.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                Drivers.Add(new MatrixTile
                {
                    Id = kv.Key,
                    Title = kv.Key,
                    Meta = kv.Value.Type,
                    Mode = kv.Value.IsConnected ? "ok" : "bad",
                });
            }

            Devices.Clear();
            foreach (var d in snap.Devices.Values.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
            {
                Devices.Add(new MatrixTile
                {
                    Id = d.Id,
                    Title = d.Name,
                    Meta = $"{d.Type} · {d.State}",
                    Mode = d.DriverConnected ? "ok" : "warn",
                });
            }
        }

        RaiseAll();
    }

    private void RaiseAll()
    {
        RaisePropertyChanged(nameof(ProjectName));
        RaisePropertyChanged(nameof(RuntimeStatus));
        RaisePropertyChanged(nameof(DriverOnline));
        RaisePropertyChanged(nameof(DeviceOnline));
        RaisePropertyChanged(nameof(LastUpdate));
        RaisePropertyChanged(nameof(OpState));
        RaisePropertyChanged(nameof(OpMsg));
        RaisePropertyChanged(nameof(OpLamp));
        RaisePropertyChanged(nameof(Recipe));
    }
}

public sealed class MonitorIoViewModel : LiveToolViewModel
{
    private string _filter = "";
    private bool _showLeds = true;

    public MonitorIoViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        ShowLedCommand = new DelegateCommand(() => ShowLeds = true);
        ShowTableCommand = new DelegateCommand(() => ShowLeds = false);
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                Reload();
            }
        }
    }

    public bool ShowLeds
    {
        get => _showLeds;
        set
        {
            if (SetProperty(ref _showLeds, value))
            {
                RaisePropertyChanged(nameof(ShowTable));
            }
        }
    }

    public bool ShowTable => !ShowLeds;

    public string LastUpdate { get; private set; } = "—";
    public ObservableCollection<IoPointRow> Inputs { get; } = [];
    public ObservableCollection<IoPointRow> Outputs { get; } = [];
    public ObservableCollection<IoLedGroup> InputGroups { get; } = [];
    public ObservableCollection<IoLedGroup> OutputGroups { get; } = [];
    public DelegateCommand ShowLedCommand { get; }
    public DelegateCommand ShowTableCommand { get; }

    protected override void Reload()
    {
        Inputs.Clear();
        Outputs.Clear();
        var snap = Runtime.LatestSnapshot;
        if (snap is null)
        {
            return;
        }

        var q = Filter.Trim();
        foreach (var d in snap.Devices.Values.Where(DeviceKind.IsGpio))
        {
            foreach (var p in d.GpioIoPoints ?? [])
            {
                if (!string.IsNullOrEmpty(q)
                    && !($"{d.Id} {d.Name} {p.Alias} {p.DriverId} {p.Address} {p.Value}"
                        .Contains(q, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

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
                if (row.IsOutput)
                {
                    Outputs.Add(row);
                }
                else
                {
                    Inputs.Add(row);
                }
            }
        }

        InputGroups.Clear();
        OutputGroups.Clear();
        foreach (var g in Inputs.GroupBy(p => p.DeviceId))
        {
            InputGroups.Add(new IoLedGroup
            {
                Title = g.First().DeviceName,
                Hint = g.Key,
                Points = g.ToList(),
            });
        }

        foreach (var g in Outputs.GroupBy(p => p.DeviceId))
        {
            OutputGroups.Add(new IoLedGroup
            {
                Title = g.First().DeviceName,
                Hint = g.Key,
                Points = g.ToList(),
            });
        }

        LastUpdate = DateTime.Now.ToString("HH:mm:ss");
        RaisePropertyChanged(nameof(LastUpdate));
    }
}

public sealed class MonitorPlatformViewModel : LiveToolViewModel
{
    private PlatformRow? _selected;

    public MonitorPlatformViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public string KpiPlat { get; private set; } = "0";
    public string KpiOnline { get; private set; } = "0";
    public string KpiAxes { get; private set; } = "0";
    public string KpiEn { get; private set; } = "0";
    public string FaceplateTitle { get; private set; } = "未选择";
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

    protected override void Reload()
    {
        var keep = Selected?.Id ?? PreferredDeviceId;
        var snap = Runtime.LatestSnapshot;
        var list = snap?.Devices.Values.Where(DeviceKind.IsPlatform).ToList() ?? [];
        Platforms.Clear();
        foreach (var d in list.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
        {
            Platforms.Add(new PlatformRow
            {
                Id = d.Id,
                Name = d.Name,
                Type = d.Type,
                Online = DeviceKind.OnlineText(d.DriverConnected),
                State = d.State,
                AxisCount = d.PlatformAxes?.Count ?? 0,
                EnabledCount = d.PlatformAxes?.Count(a => a.MotionEnabled == true) ?? 0,
            });
        }

        KpiPlat = Platforms.Count.ToString();
        KpiOnline = list.Count(d => d.DriverConnected).ToString();
        KpiAxes = list.Sum(d => d.PlatformAxes?.Count ?? 0).ToString();
        KpiEn = list.Sum(d => d.PlatformAxes?.Count(a => a.MotionEnabled == true) ?? 0).ToString();
        Selected = Platforms.FirstOrDefault(p => string.Equals(p.Id, keep, StringComparison.OrdinalIgnoreCase))
                   ?? Platforms.FirstOrDefault();
        RaisePropertyChanged(nameof(KpiPlat));
        RaisePropertyChanged(nameof(KpiOnline));
        RaisePropertyChanged(nameof(KpiAxes));
        RaisePropertyChanged(nameof(KpiEn));
    }

    private void LoadAxes()
    {
        Axes.Clear();
        FaceplateTitle = Selected is null ? "未选择" : $"{Selected.Name} ({Selected.Id})";
        RaisePropertyChanged(nameof(FaceplateTitle));
        if (Selected is null || Runtime.LatestSnapshot is null)
        {
            return;
        }

        if (!Runtime.LatestSnapshot.Devices.TryGetValue(Selected.Id, out var d) || d.PlatformAxes is null)
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
}

public sealed class MonitorAxisViewModel : LiveToolViewModel
{
    private string _filter = "";
    private AxisRow? _selected;

    public MonitorAxisViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                Reload();
            }
        }
    }

    public string KpiTotal { get; private set; } = "0";
    public string KpiOnline { get; private set; } = "0";
    public string KpiEnabled { get; private set; } = "0";
    public string KpiAlarm { get; private set; } = "0";
    public string FpTitle { get; private set; } = "未选择";
    public string FpState { get; private set; } = "—";
    public string FpDriver { get; private set; } = "—";
    public string FpOnline { get; private set; } = "—";
    public string FpEnabled { get; private set; } = "—";
    public string FpPrf { get; private set; } = "—";
    public string FpEnc { get; private set; } = "—";
    public string FpVel { get; private set; } = "—";
    public string FpRaw { get; private set; } = "—";
    public string FpFlags { get; private set; } = "—";
    public ObservableCollection<AxisRow> Axes { get; } = [];

    public AxisRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                LoadFaceplate();
            }
        }
    }

    protected override void Reload()
    {
        var keep = Selected?.Id ?? PreferredDeviceId;
        var snap = Runtime.LatestSnapshot;
        var all = snap?.Devices.Values.Where(DeviceKind.IsAxis).ToList() ?? [];
        var q = Filter.Trim();
        Axes.Clear();
        foreach (var d in all.Where(d =>
                     string.IsNullOrEmpty(q)
                     || $"{d.Id} {d.Name}".Contains(q, StringComparison.OrdinalIgnoreCase)))
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

        KpiTotal = all.Count.ToString();
        KpiOnline = all.Count(d => d.DriverConnected).ToString();
        KpiEnabled = all.Count(d => d.AxisStatus?.ServoOn == true).ToString();
        KpiAlarm = all.Count(d => d.AxisStatus?.Alarm == true).ToString();
        Selected = Axes.FirstOrDefault(a => string.Equals(a.Id, keep, StringComparison.OrdinalIgnoreCase))
                   ?? Axes.FirstOrDefault();
        RaisePropertyChanged(nameof(KpiTotal));
        RaisePropertyChanged(nameof(KpiOnline));
        RaisePropertyChanged(nameof(KpiEnabled));
        RaisePropertyChanged(nameof(KpiAlarm));
    }

    private void LoadFaceplate()
    {
        FpTitle = Selected is null ? "未选择" : $"{Selected.Name} ({Selected.Id})";
        if (Selected is null || Runtime.LatestSnapshot is null
            || !Runtime.LatestSnapshot.Devices.TryGetValue(Selected.Id, out var d))
        {
            FpState = FpDriver = FpOnline = FpEnabled = FpPrf = FpEnc = FpVel = FpRaw = FpFlags = "—";
            RaiseFp();
            return;
        }

        var st = d.AxisStatus;
        FpState = d.State;
        FpDriver = d.DriverType;
        FpOnline = DeviceKind.OnlineText(d.DriverConnected);
        FpEnabled = DeviceKind.EnabledText(st?.ServoOn);
        FpPrf = DeviceKind.Fmt(st?.PrfPosition);
        FpEnc = DeviceKind.Fmt(st?.EncPosition);
        FpVel = DeviceKind.Fmt(st?.Velocity);
        FpRaw = st is null ? "—" : $"0x{st.Value.Raw:X4}";
        FpFlags = DeviceKind.AxisFlags(st);
        RaiseFp();
    }

    private void RaiseFp()
    {
        RaisePropertyChanged(nameof(FpTitle));
        RaisePropertyChanged(nameof(FpState));
        RaisePropertyChanged(nameof(FpDriver));
        RaisePropertyChanged(nameof(FpOnline));
        RaisePropertyChanged(nameof(FpEnabled));
        RaisePropertyChanged(nameof(FpPrf));
        RaisePropertyChanged(nameof(FpEnc));
        RaisePropertyChanged(nameof(FpVel));
        RaisePropertyChanged(nameof(FpRaw));
        RaisePropertyChanged(nameof(FpFlags));
    }
}

public sealed class MonitorCameraViewModel : LiveToolViewModel
{
    private CameraRow? _selected;

    public MonitorCameraViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public string KpiTotal { get; private set; } = "0";
    public string KpiOnline { get; private set; } = "0";
    public string KpiRun { get; private set; } = "0";
    public string FpTitle { get; private set; } = "未选择";
    public string FpState { get; private set; } = "—";
    public string FpType { get; private set; } = "—";
    public string FpOnline { get; private set; } = "—";
    public string FpLink { get; private set; } = "—";
    public ObservableCollection<CameraRow> Cameras { get; } = [];

    public CameraRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                LoadFp();
            }
        }
    }

    protected override void Reload()
    {
        var keep = Selected?.Id ?? PreferredDeviceId;
        var list = Runtime.LatestSnapshot?.Devices.Values.Where(DeviceKind.IsCamera).ToList() ?? [];
        Cameras.Clear();
        foreach (var d in list)
        {
            Cameras.Add(new CameraRow
            {
                Id = d.Id,
                Name = d.Name,
                Type = d.Type,
                State = d.State,
                Online = DeviceKind.OnlineText(d.DriverConnected),
                Driver = d.DriverType,
            });
        }

        KpiTotal = list.Count.ToString();
        KpiOnline = list.Count(d => d.DriverConnected).ToString();
        KpiRun = list.Count(d => d.State.Contains("run", StringComparison.OrdinalIgnoreCase)
                                 || d.State.Contains("open", StringComparison.OrdinalIgnoreCase)).ToString();
        Selected = Cameras.FirstOrDefault(c => string.Equals(c.Id, keep, StringComparison.OrdinalIgnoreCase))
                   ?? Cameras.FirstOrDefault();
        RaisePropertyChanged(nameof(KpiTotal));
        RaisePropertyChanged(nameof(KpiOnline));
        RaisePropertyChanged(nameof(KpiRun));
    }

    private void LoadFp()
    {
        FpTitle = Selected is null ? "未选择" : $"{Selected.Name} ({Selected.Id})";
        FpState = Selected?.State ?? "—";
        FpType = Selected?.Type ?? "—";
        FpOnline = Selected?.Online ?? "—";
        FpLink = Selected?.Driver ?? "—";
        RaisePropertyChanged(nameof(FpTitle));
        RaisePropertyChanged(nameof(FpState));
        RaisePropertyChanged(nameof(FpType));
        RaisePropertyChanged(nameof(FpOnline));
        RaisePropertyChanged(nameof(FpLink));
    }
}

public sealed class MonitorVisionViewModel : LiveToolViewModel
{
    public MonitorVisionViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public string ActiveVision { get; private set; } = "—";
    public string VState { get; private set; } = "—";
    public string VOk { get; private set; } = "—";
    public string VScore { get; private set; } = "—";
    public string VCount { get; private set; } = "—";
    public ObservableCollection<VisionDefRow> Visions { get; } = [];
    public ObservableCollection<KvRow> Vars { get; } = [];

    protected override void Reload()
    {
        var vars = SnapshotReader.Vars(Runtime.LatestSnapshot);
        ActiveVision = Runtime.Runtime.Setting.ActiveVisionId ?? "—";
        VState = SnapshotReader.VarStr(vars, "vision.state");
        VOk = SnapshotReader.VarStr(vars, "vision.ok");
        VScore = SnapshotReader.VarStr(vars, "vision.score");
        VCount = SnapshotReader.VarStr(vars, "vision.count", SnapshotReader.VarStr(vars, "vision.runCount"));
        Visions.Clear();
        foreach (var v in Runtime.Runtime.Setting.Visions)
        {
            Visions.Add(new VisionDefRow
            {
                Id = v.Id,
                Name = v.Name,
                Camera = v.CameraDeviceId,
                Nodes = (v.Pipeline?.Nodes.Count ?? 0).ToString(),
                Description = v.Description ?? "",
            });
        }

        Vars.Clear();
        foreach (var kv in vars.Where(k => k.Key.StartsWith("vision.", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            Vars.Add(new KvRow { Key = kv.Key, Value = kv.Value?.ToString() ?? "" });
        }

        RaisePropertyChanged(nameof(ActiveVision));
        RaisePropertyChanged(nameof(VState));
        RaisePropertyChanged(nameof(VOk));
        RaisePropertyChanged(nameof(VScore));
        RaisePropertyChanged(nameof(VCount));
    }
}

public sealed class MonitorTaskViewModel : LiveToolViewModel
{
    public MonitorTaskViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public string OpState { get; private set; } = "—";
    public string OpLamp { get; private set; } = "—";
    public string KpiCycle { get; private set; } = "—";
    public string KpiCt { get; private set; } = "—";
    public string KpiFault { get; private set; } = "—";
    public string OpMsg { get; private set; } = "—";
    public ObservableCollection<TaskRow> Tasks { get; } = [];
    public ObservableCollection<KvRow> CycleVars { get; } = [];

    protected override void Reload()
    {
        var vars = SnapshotReader.Vars(Runtime.LatestSnapshot);
        OpState = SnapshotReader.VarStr(vars, "task.operation.state");
        OpLamp = SnapshotReader.VarStr(vars, "task.operation.lamp");
        KpiCycle = SnapshotReader.VarStr(vars, "task.cycle.count");
        KpiCt = SnapshotReader.VarStr(vars, "task.cycle.ct", SnapshotReader.VarStr(vars, "task.cycle.cycleTimeMs"));
        KpiFault = SnapshotReader.VarStr(vars, "task.cycle.dev.fault");
        OpMsg = SnapshotReader.VarStr(vars, "task.operation.message", SnapshotReader.VarStr(vars, "task.operation.msg"));
        Tasks.Clear();
        foreach (var t in Runtime.ListTasks())
        {
            Tasks.Add(new TaskRow { Name = t.Name, Type = t.Type, IntervalMs = t.IntervalMs, State = t.State });
        }

        CycleVars.Clear();
        foreach (var kv in vars.Where(k => k.Key.StartsWith("task.cycle.", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            CycleVars.Add(new KvRow { Key = kv.Key, Value = kv.Value?.ToString() ?? "" });
        }

        RaisePropertyChanged(nameof(OpState));
        RaisePropertyChanged(nameof(OpLamp));
        RaisePropertyChanged(nameof(KpiCycle));
        RaisePropertyChanged(nameof(KpiCt));
        RaisePropertyChanged(nameof(KpiFault));
        RaisePropertyChanged(nameof(OpMsg));
    }
}

public sealed class MonitorAlarmViewModel : LiveToolViewModel
{
    private string _filter = "";
    private string _scope = "active";

    public MonitorAlarmViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
            {
                Reload();
            }
        }
    }

    public string Scope
    {
        get => _scope;
        set
        {
            if (SetProperty(ref _scope, value))
            {
                Reload();
            }
        }
    }

    public string VActive { get; private set; } = "0";
    public string VError { get; private set; } = "0";
    public string VWarn { get; private set; } = "0";
    public IReadOnlyList<KvRow> Scopes { get; } =
    [
        new() { Key = "active", Value = "仅活动" },
        new() { Key = "all", Value = "全部目录" },
    ];
    public ObservableCollection<AlarmMonitorRow> Items { get; } = [];

    protected override void Reload()
    {
        var active = Runtime.ListActiveAlarms();
        var catalog = Runtime.Runtime.Setting.Alarms;
        var vars = SnapshotReader.Vars(Runtime.LatestSnapshot);
        var rows = new List<AlarmMonitorRow>();
        foreach (var a in catalog)
        {
            var isActive = active.Any(x => string.Equals(x.EffectiveId, a.EffectiveId, StringComparison.OrdinalIgnoreCase));
            rows.Add(ToRow(a, isActive, vars));
        }

        foreach (var a in active.Where(a => rows.All(r => !string.Equals(r.Id, a.EffectiveId, StringComparison.OrdinalIgnoreCase))))
        {
            rows.Add(ToRow(a, true, vars));
        }

        var q = Filter.Trim();
        Items.Clear();
        foreach (var r in rows.Where(r =>
                     (Scope != "active" || r.Active)
                     && (string.IsNullOrEmpty(q)
                         || $"{r.Code} {r.Name} {r.Message} {r.VarKey} {r.Id}"
                             .Contains(q, StringComparison.OrdinalIgnoreCase))))
        {
            Items.Add(r);
        }

        VActive = active.Count.ToString();
        VError = active.Count(a => a.Level.Equals("error", StringComparison.OrdinalIgnoreCase)).ToString();
        VWarn = active.Count(a => a.Level.Equals("warn", StringComparison.OrdinalIgnoreCase)).ToString();
        RaisePropertyChanged(nameof(VActive));
        RaisePropertyChanged(nameof(VError));
        RaisePropertyChanged(nameof(VWarn));
    }

    private static AlarmMonitorRow ToRow(MdkSetting.AlarmConfig a, bool active, IReadOnlyDictionary<string, object?> vars) =>
        new()
        {
            Id = a.EffectiveId,
            Code = a.Code,
            Name = a.Name,
            Level = a.Level,
            Message = a.EffectiveMessage,
            VarKey = a.VarKey,
            Value = SnapshotReader.VarStr(vars, a.VarKey, ""),
            Active = active,
        };
}
