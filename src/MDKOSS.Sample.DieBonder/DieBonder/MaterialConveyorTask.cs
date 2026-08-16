using System.Globalization;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Pnp;
using MDKOSS.Tasks;

namespace MDKOSS.Sample.DieBonder.Machine;

/// <summary>
/// 晶圆盘 / 基板换盘：响应 <c>task.pnp.trayChangeRequest</c>，
/// 脉冲传送带输出并重置 tray nest。
/// </summary>
public sealed class MaterialConveyorTask : MotionTask
{
    private enum Phase
    {
        Idle,
        RunConveyors,
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

    public MaterialConveyorTask(
        string name,
        int intervalMs,
        IDriver driver,
        MVarStore vars,
        IReadOnlyDictionary<string, MDeviceBase> devices,
        IReadOnlyDictionary<string, string>? parameters = null,
        MdkAlarmManager? alarms = null)
        : base(name, intervalMs, driver, vars, devices, alarms)
    {
        parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _gpioId = Read(parameters, "gpioDeviceId", "gpio-machine");
        _sourceTrayId = Read(parameters, "sourceTrayDeviceId", "tray-wafer");
        _targetTrayId = Read(parameters, "targetTrayDeviceId", "tray-substrate");
        _srcRunAlias = Read(parameters, "sourceRunAlias", "wafer.conveyorRun");
        _tgtRunAlias = Read(parameters, "targetRunAlias", "substrate.conveyorRun");
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
            Enter(Phase.RunConveyors, "advance wafer/substrate conveyors");
            SetGlobalVar("task.pnp.srcTrayPresent", false);
            SetGlobalVar("task.pnp.tgtTrayPresent", false);
            GpioWriteOutput(_gpioId, _srcRunAlias, true);
            GpioWriteOutput(_gpioId, _tgtRunAlias, true);
            _ticksLeft = _runTicks;
        }

        switch (_phase)
        {
            case Phase.RunConveyors:
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
            BondLogStore.Info("conveyor", $"[{phase}] {message}");
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
        SetGlobalVar("task.bond.conveyor.phase", _phase.ToString());
        SetGlobalVar("task.bond.conveyor.message", _message);
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
