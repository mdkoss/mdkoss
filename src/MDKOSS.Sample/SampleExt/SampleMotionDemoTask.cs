using System.Globalization;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Tasks;

namespace MDKOSS.Sample.SampleExt;

/// <summary>
/// MotionTask example: enable → move → jog → stop on a platform axis.
/// Driven by <c>sample.motion.command</c> (start / stop / reset).
/// </summary>
public sealed class SampleMotionDemoTask : MotionTask
{
    private enum Phase
    {
        Idle,
        Enable,
        Move,
        Jog,
        Stop,
        Done,
        Fault,
    }

    private readonly string _platformId;
    private readonly string _axisLetter;
    private readonly string _beaconId;
    private readonly double _targetPos;
    private readonly double _jogVelocity;
    private readonly int _jogTicks;

    private Phase _phase = Phase.Idle;
    private int _ticksLeft;
    private string _message = "idle";
    private int _cycleCount;

    public SampleMotionDemoTask(
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
        _platformId = Read(parameters, "platformDeviceId", "head-bond");
        _axisLetter = Read(parameters, "axisLetter", "X");
        _beaconId = Read(parameters, "beaconDeviceId", "sample-beacon");
        _targetPos = ReadDouble(parameters, "targetPos", 5);
        _jogVelocity = ReadDouble(parameters, "jogVelocity", 2);
        _jogTicks = Math.Max(1, ReadInt(parameters, "jogTicks", 3));

        foreach (var kv in parameters)
        {
            SetParam(kv.Key, kv.Value);
        }

        Publish();
    }

    protected override Task TickAsync(CancellationToken cancellationToken)
    {
        var command = GetGlobalVar<string>("sample.motion.command");
        if (!string.IsNullOrWhiteSpace(command))
        {
            HandleCommand(command.Trim());
            SetGlobalVar("sample.motion.command", string.Empty);
        }

        if (_phase is Phase.Idle or Phase.Done or Phase.Fault)
        {
            Publish();
            return Task.CompletedTask;
        }

        if (_ticksLeft > 0)
        {
            _ticksLeft--;
            Publish();
            return Task.CompletedTask;
        }

        try
        {
            Step();
        }
        catch (Exception ex)
        {
            Enter(Phase.Fault, ex.Message);
        }

        Publish();
        return Task.CompletedTask;
    }

    private void HandleCommand(string command)
    {
        switch (command.ToLowerInvariant())
        {
            case "start":
                if (_phase is Phase.Idle or Phase.Done or Phase.Fault)
                {
                    Enter(Phase.Enable, "enable platform motion");
                }
                break;
            case "stop":
                PlatformAxisStopMotion(_platformId, _axisLetter);
                PlatformStopMotion(_platformId);
                Enter(Phase.Idle, "stopped by command");
                break;
            case "reset":
                PlatformAxisStopMotion(_platformId, _axisLetter);
                PlatformStopMotion(_platformId);
                _cycleCount = 0;
                Enter(Phase.Idle, "reset");
                break;
            default:
                _message = $"unknown command: {command}";
                break;
        }
    }

    private void Step()
    {
        switch (_phase)
        {
            case Phase.Enable:
                if (!PlatformStartMotion(_platformId))
                {
                    Enter(Phase.Fault, $"failed to enable {_platformId}");
                    return;
                }

                Enter(Phase.Move, $"move {_axisLetter} to {_targetPos}", 1);
                break;

            case Phase.Move:
                if (!PlatformAxisMoveTo(_platformId, _axisLetter, _targetPos))
                {
                    Enter(Phase.Fault, $"move failed on {_axisLetter}");
                    return;
                }

                Enter(Phase.Jog, $"jog {_axisLetter}", _jogTicks);
                break;

            case Phase.Jog:
                if (!PlatformAxisJog(_platformId, _axisLetter, +1, _jogVelocity))
                {
                    Enter(Phase.Fault, $"jog failed on {_axisLetter}");
                    return;
                }

                Enter(Phase.Stop, "stop jog", 1);
                break;

            case Phase.Stop:
                PlatformAxisStopMotion(_platformId, _axisLetter);
                PulseBeacon("motion demo cycle done");
                _cycleCount++;
                Enter(Phase.Done, $"cycle #{_cycleCount} done");
                break;
        }
    }

    private void PulseBeacon(string note)
    {
        if (TryGetDevice<SampleBeaconDevice>(_beaconId, out var beacon) && beacon is not null)
        {
            beacon.Pulse(note);
        }
    }

    private void Enter(Phase phase, string message, int dwell = 0)
    {
        _phase = phase;
        _message = message;
        _ticksLeft = dwell;
        SetVar("phase", phase.ToString());
        SetVar("message", message);
    }

    private void Publish()
    {
        SetVar("phase", _phase.ToString());
        SetVar("message", _message);
        SetVar("cycleCount", _cycleCount);
        SetVar("alive", true);
        SetVar("lastTickUtc", DateTime.UtcNow);
        SetGlobalVar("sample.motion.phase", _phase.ToString());
        SetGlobalVar("sample.motion.message", _message);
        SetGlobalVar("sample.motion.cycleCount", _cycleCount);
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

    private static double ReadDouble(IReadOnlyDictionary<string, string> parameters, string key, double fallback)
    {
        if (!parameters.TryGetValue(key, out var raw)
            || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return fallback;
        }

        return value;
    }
}
