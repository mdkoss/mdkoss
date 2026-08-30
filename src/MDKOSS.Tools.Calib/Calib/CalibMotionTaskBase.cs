using System.Globalization;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Tasks;

namespace MDKOSS.Tools.Calib.Calib;

/// <summary>
/// Command-driven calibration MotionTask. UI writes <c>task.{name}.command</c>
/// (<c>start</c> / <c>stop</c> / <c>reset</c>) and reads <c>calib.*</c> results.
/// </summary>
public abstract class CalibMotionTaskBase : MotionTask
{
    protected enum CalibPhase
    {
        Idle,
        Running,
        Done,
        Fault,
    }

    private readonly Dictionary<string, string> _configParams;

    protected CalibMotionTaskBase(
        string name,
        int intervalMs,
        IDriver driver,
        MVarStore vars,
        IReadOnlyDictionary<string, MDeviceBase> devices,
        IReadOnlyDictionary<string, string>? parameters = null,
        MdkAlarmManager? alarms = null)
        : base(name, intervalMs, driver, vars, devices, alarms)
    {
        _configParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (parameters is not null)
        {
            foreach (var kv in parameters)
            {
                _configParams[kv.Key] = kv.Value;
                SetParam(kv.Key, kv.Value);
            }
        }

        Phase = CalibPhase.Idle;
        Message = "就绪";
        Publish();
    }

    protected CalibPhase Phase { get; private set; }

    protected string Message { get; private set; } = "";

    protected override Task TickAsync(CancellationToken cancellationToken)
    {
        var command = (GetVar<string>("command") ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(command))
        {
            SetVar("command", string.Empty);
            HandleCommand(command);
        }

        if (Phase == CalibPhase.Running)
        {
            try
            {
                Step();
            }
            catch (Exception ex)
            {
                Enter(CalibPhase.Fault, ex.Message);
                SetResult("ok", false);
                SetResult("message", ex.Message);
            }
        }

        Publish();
        return Task.CompletedTask;
    }

    protected abstract void OnStart();

    protected abstract void Step();

    protected virtual void OnStop()
    {
    }

    protected virtual void OnReset()
    {
    }

    protected void Enter(CalibPhase phase, string message)
    {
        Phase = phase;
        Message = message;
        SetVar("phase", phase.ToString());
        SetVar("message", message);
    }

    protected void SetResult(string key, object? value) => SetVar("calib." + key, value);

    protected string ReadString(string key, string fallback)
    {
        if (TryReadLive(key, out var live) && !string.IsNullOrWhiteSpace(live))
        {
            return live.Trim();
        }

        return _configParams.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;
    }

    protected int ReadInt(string key, int fallback)
    {
        var raw = ReadString(key, fallback.ToString(CultureInfo.InvariantCulture));
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : fallback;
    }

    protected double ReadDouble(string key, double fallback)
    {
        var raw = ReadString(key, fallback.ToString(CultureInfo.InvariantCulture));
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;
    }

    protected double ReadAxisPosition(AxisDevice axis)
    {
        var snap = axis.GetSnapshot();
        if (snap.AxisStatus is { } status)
        {
            if (Math.Abs(status.EncPosition) > 1e-9)
            {
                return status.EncPosition;
            }

            if (Math.Abs(status.PrfPosition) > 1e-9)
            {
                return status.PrfPosition;
            }
        }

        return GetGlobalVar<double>($"device.{axis.Name}.{axis.Id}.position");
    }

    private void HandleCommand(string command)
    {
        switch (command.ToLowerInvariant())
        {
            case "start":
                if (Phase is CalibPhase.Idle or CalibPhase.Done or CalibPhase.Fault)
                {
                    ReloadLiveParams();
                    Enter(CalibPhase.Running, "开始标定");
                    OnStart();
                }

                break;
            case "stop":
                OnStop();
                Enter(CalibPhase.Idle, "已停止");
                break;
            case "reset":
                OnStop();
                OnReset();
                Enter(CalibPhase.Idle, "已复位");
                SetResult("ok", false);
                SetResult("message", "");
                break;
            default:
                Message = "未知命令: " + command;
                break;
        }
    }

    private void ReloadLiveParams()
    {
        foreach (var key in _configParams.Keys.ToList())
        {
            if (TryReadLive(key, out var live) && live is not null)
            {
                _configParams[key] = live;
                SetParam(key, live);
            }
        }
    }

    private bool TryReadLive(string key, out string? value)
    {
        value = GetVar<string>("param." + key);
        return value is not null;
    }

    private void Publish()
    {
        SetVar("phase", Phase.ToString());
        SetVar("message", Message);
        SetVar("alive", true);
        SetVar("lastTickUtc", DateTime.UtcNow);
    }
}
