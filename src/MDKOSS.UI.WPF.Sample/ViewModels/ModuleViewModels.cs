using System.Collections.ObjectModel;
using System.Text.Json;
using MDKOSS.UI.WPF.Infrastructure;
using MDKOSS.UI.WPF.Models;
using MDKOSS.UI.WPF.Services;
using MDKOSS.UI.WPF.ViewModels.Tools;
using Prism.Commands;

namespace MDKOSS.UI.WPF.Sample.ViewModels;

public sealed class MonitorSampleExtViewModel : LiveToolViewModel
{
    public MonitorSampleExtViewModel(IRuntimeUiService runtime) : base(runtime) { }

    public string BeaconLabel { get; private set; } = "—";
    public string PulseCount { get; private set; } = "0";
    public string BeaconMessage { get; private set; } = "—";
    public string MotionPhase { get; private set; } = "—";
    public string MotionMessage { get; private set; } = "—";
    public string MotionCycles { get; private set; } = "0";

    protected override void Reload()
    {
        var vars = SnapshotReader.Vars(Runtime.LatestSnapshot);
        BeaconLabel = SnapshotReader.VarStr(vars, "sample.beacon.label", "—");
        PulseCount = SnapshotReader.VarStr(vars, "sample.beacon.pulseCount", "0");
        BeaconMessage = SnapshotReader.VarStr(vars, "sample.beacon.message", "—");
        MotionPhase = SnapshotReader.VarStr(vars, "sample.motion.phase", "—");
        MotionMessage = SnapshotReader.VarStr(vars, "sample.motion.message", "—");
        MotionCycles = SnapshotReader.VarStr(vars, "sample.motion.cycleCount", "0");
        RaisePropertyChanged(nameof(BeaconLabel));
        RaisePropertyChanged(nameof(PulseCount));
        RaisePropertyChanged(nameof(BeaconMessage));
        RaisePropertyChanged(nameof(MotionPhase));
        RaisePropertyChanged(nameof(MotionMessage));
        RaisePropertyChanged(nameof(MotionCycles));
    }
}

public sealed class DebugSampleExtViewModel : LiveToolViewModel
{
    private CameraRow? _selected;
    private string _note = "wpf pulse";

    public DebugSampleExtViewModel(IRuntimeUiService runtime) : base(runtime)
    {
        PulseCommand = new DelegateCommand(() =>
            Act("pulse", new Dictionary<string, object?> { ["note"] = Note }));
        ResetCommand = new DelegateCommand(() => Act("reset", confirm: true));
        MotionStartCommand = new DelegateCommand(() => SetMotion("start"));
        MotionStopCommand = new DelegateCommand(() => SetMotion("stop"));
    }

    public ObservableCollection<CameraRow> Devices { get; } = [];

    public CameraRow? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public string Note
    {
        get => _note;
        set => SetProperty(ref _note, value);
    }

    public string BeaconMessage { get; private set; } = "—";
    public string MotionPhase { get; private set; } = "—";
    public DelegateCommand PulseCommand { get; }
    public DelegateCommand ResetCommand { get; }
    public DelegateCommand MotionStartCommand { get; }
    public DelegateCommand MotionStopCommand { get; }

    protected override void Reload()
    {
        var keep = Selected?.Id ?? PreferredDeviceId;
        Devices.Clear();
        foreach (var d in Runtime.LatestSnapshot?.Devices.Values.Where(DeviceKind.IsSampleBeacon) ?? [])
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
        BeaconMessage = SnapshotReader.VarStr(vars, "sample.beacon.message", "—");
        MotionPhase = SnapshotReader.VarStr(vars, "sample.motion.phase", "—");
        RaisePropertyChanged(nameof(BeaconMessage));
        RaisePropertyChanged(nameof(MotionPhase));
    }

    private void Act(string action, Dictionary<string, object?>? p = null, bool confirm = false)
    {
        if (Selected is null || (confirm && !DeviceKind.ConfirmWrite($"确认 {action}？")))
        {
            return;
        }

        ToastResult(Runtime.ExecuteAction(Selected.Id, action, p), action);
    }

    private void SetMotion(string command)
    {
        Runtime.Runtime.Vars.Set("sample.motion.command", command);
        Runtime.Refresh();
        Toast($"motion {command} 已下发");
    }

    private void ToastResult(MDKOSS.Core.DeviceActionResult r, string action) =>
        Toast(r.Success ? $"{action} 成功 {FormatData(r.Data)}" : r.Error ?? "失败", r.Success);

    private static string FormatData(object? data) =>
        data is null ? "" : JsonSerializer.Serialize(data);
}
