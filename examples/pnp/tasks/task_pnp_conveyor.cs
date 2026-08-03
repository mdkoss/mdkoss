using System.Globalization;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Tasks;

namespace MDKOSS.Pnp;

/// <summary>
/// Conveyor / tray exchange helper: when the cycle requests a tray change,
/// pulse conveyor outputs, wait for present sensors, then reset tray nest maps.
/// </summary>
public sealed class PnpConveyorTask : MotionTask
{
    private enum Phase
    {
        Idle,
        RunSource,
        RunTarget,
        WaitPresent,
        Complete
    }

    private readonly string _gpioId;
    private readonly string _sourceTrayId;
    private readonly string _targetTrayId;
    private readonly string _srcRunAlias;
    private readonly string _tgtRunAlias;
    private readonly int _runTicks;

    private Phase _phase = Phase.Idle;
    private int _ticksLeft;
    private string _message = "idle";

    public PnpConveyorTask(
        string name,
        int intervalMs,
        IDriver driver,
        MVarStore vars,
        IReadOnlyDictionary<string, MDeviceBase> devices,
        IReadOnlyDictionary<string, string>? parameters = null)
        : base(name, intervalMs, driver, vars, devices)
    {
        parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _gpioId = Read(parameters, "gpioDeviceId", "gpio-pnp");
        _sourceTrayId = Read(parameters, "sourceTrayDeviceId", "tray-source");
        _targetTrayId = Read(parameters, "targetTrayDeviceId", "tray-target");
        _srcRunAlias = Read(parameters, "sourceRunAlias", "src.conveyorRun");
        _tgtRunAlias = Read(parameters, "targetRunAlias", "tgt.conveyorRun");
        _runTicks = Math.Max(1, ReadInt(parameters, "runTicks", 5));

        foreach (var kv in parameters)
        {
            SetParam(kv.Key, kv.Value);
        }

        Publish();
    }

    protected override Task TickAsync(CancellationToken cancellationToken)
    {
        var request = GetGlobalVar<bool>("task.pnp.trayChangeRequest");
        if (_phase == Phase.Idle && request)
        {
            Enter(Phase.RunSource, "advance source/target conveyors");
            SetGlobalVar("task.pnp.srcTrayPresent", false);
            SetGlobalVar("task.pnp.tgtTrayPresent", false);
            GpioWriteOutput(_gpioId, _srcRunAlias, true);
            GpioWriteOutput(_gpioId, _tgtRunAlias, true);
            _ticksLeft = _runTicks;
        }

        switch (_phase)
        {
            case Phase.RunSource:
            case Phase.RunTarget:
                _ticksLeft--;
                if (_ticksLeft <= 0)
                {
                    GpioWriteOutput(_gpioId, _srcRunAlias, false);
                    GpioWriteOutput(_gpioId, _tgtRunAlias, false);
                    SetGlobalVar("task.pnp.srcTrayPresent", true);
                    SetGlobalVar("task.pnp.tgtTrayPresent", true);
                    Enter(Phase.WaitPresent, "wait tray present");
                    _ticksLeft = 2;
                }
                break;

            case Phase.WaitPresent:
                _ticksLeft--;
                var srcOk = GetGlobalVar<bool>("task.pnp.srcTrayPresent");
                var tgtOk = GetGlobalVar<bool>("task.pnp.tgtTrayPresent");
                if ((srcOk && tgtOk) || _ticksLeft <= 0)
                {
                    if (TryGetDevice<TrayDevice>(_sourceTrayId, out var srcTray) && srcTray is not null)
                    {
                        srcTray.MarkTrayChanged();
                    }

                    if (TryGetDevice<TrayDevice>(_targetTrayId, out var tgtTray) && tgtTray is not null)
                    {
                        tgtTray.MarkTrayChanged();
                    }

                    SetGlobalVar("task.pnp.trayChangeRequest", false);
                    SetGlobalVar("task.pnp.trayChangeUtc", DateTime.UtcNow);
                    Enter(Phase.Complete, "tray change done");
                }
                break;

            case Phase.Complete:
                Enter(Phase.Idle, "idle");
                break;
        }

        Publish();
        return Task.CompletedTask;
    }

    private void Enter(Phase phase, string message)
    {
        if (phase == _phase && string.Equals(_message, message, StringComparison.Ordinal))
        {
            return;
        }

        _phase = phase;
        _message = message;
        if (phase != Phase.Idle || !string.Equals(message, "idle", StringComparison.OrdinalIgnoreCase))
        {
            PnpLogStore.Info("conveyor", $"[{phase}] {message}");
        }
    }

    private void Publish()
    {
        SetVar("phase", _phase.ToString());
        SetVar("message", _message);
        SetVar("alive", true);
        SetVar("lastTickUtc", DateTime.UtcNow);
        SetGlobalVar("task.pnp.conveyor.phase", _phase.ToString());
        SetGlobalVar("task.pnp.conveyor.message", _message);
    }

    private static string Read(IReadOnlyDictionary<string, string> parameters, string key, string fallback)
        => parameters.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

    private static int ReadInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback)
    {
        if (!parameters.TryGetValue(key, out var raw)
            || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return fallback;
        }

        return value;
    }
}
