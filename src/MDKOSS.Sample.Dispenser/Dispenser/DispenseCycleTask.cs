using System.Globalization;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Tasks;

namespace MDKOSS.Sample.Dispenser.Machine;

/// <summary>
/// 三轴点胶主循环：等工件 → 安全高度 → XY 到位 → Z 下降 → 开阀 → 停留 → 关阀 → 抬起 → 下一点。
/// 状态写 <c>task.dispense.*</c>。
/// </summary>
public sealed class DispenseCycleTask : MotionTask
{
    private enum Phase
    {
        Idle,
        WaitWorkpiece,
        Enable,
        MoveSafeZ,
        MoveXy,
        DescendZ,
        ValveOn,
        Dwell,
        ValveOff,
        RetractZ,
        NextPoint,
        Done,
        Fault,
    }

    private readonly string _platformId;
    private readonly string _gpioId;
    private readonly string _valveAlias;
    private readonly string _workpieceAlias;
    private readonly bool _checkWorkpieceIo;
    private readonly int _defaultRows;
    private readonly int _defaultCols;
    private readonly double _defaultOriginX;
    private readonly double _defaultOriginY;
    private readonly double _defaultPitchX;
    private readonly double _defaultPitchY;
    private readonly double _defaultSafeZ;
    private readonly double _defaultDispenseZ;
    private readonly int _defaultDwellTicks;

    private Phase _phase = Phase.Idle;
    private int _dwellLeft;
    private int _pointIndex;
    private int _okCount;
    private int _ngCount;
    private bool _valveOn;
    private double _x;
    private double _y;
    private double _z;
    private string _message = "ready";
    private List<(double X, double Y)> _points = [];

    public DispenseCycleTask(
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
        _platformId = Read(parameters, "platformDeviceId", "head-dispense");
        _gpioId = Read(parameters, "gpioDeviceId", "gpio-machine");
        _valveAlias = Read(parameters, "valveAlias", "valve");
        _workpieceAlias = Read(parameters, "workpieceAlias", "workpiece.present");
        _checkWorkpieceIo = ReadBool(parameters, "checkWorkpieceIo", false);
        _defaultRows = Math.Max(1, ReadInt(parameters, "rows", 2));
        _defaultCols = Math.Max(1, ReadInt(parameters, "cols", 2));
        _defaultOriginX = ReadDouble(parameters, "originX", 10);
        _defaultOriginY = ReadDouble(parameters, "originY", 10);
        _defaultPitchX = ReadDouble(parameters, "pitchX", 8);
        _defaultPitchY = ReadDouble(parameters, "pitchY", 8);
        _defaultSafeZ = ReadDouble(parameters, "safeZ", 0);
        _defaultDispenseZ = ReadDouble(parameters, "dispenseZ", -6);
        _defaultDwellTicks = Math.Max(1, ReadInt(parameters, "dwellTicks", 2));

        foreach (var kv in parameters)
        {
            SetParam(kv.Key, kv.Value);
        }

        Publish();
    }

    protected override Task TickAsync(CancellationToken cancellationToken)
    {
        var command = GetGlobalVar<string>("task.dispense.command");
        if (!string.IsNullOrWhiteSpace(command))
        {
            HandleCommand(command.Trim());
            SetGlobalVar("task.dispense.command", string.Empty);
        }

        var running = string.Equals(GetGlobalVar<string>("task.operation.state"), "running", StringComparison.OrdinalIgnoreCase)
                      || GetFlag("task.dispense.run");

        if (_phase is Phase.Idle or Phase.Done or Phase.Fault)
        {
            if (running && _phase == Phase.Idle)
            {
                BeginCycle("start from idle");
            }

            Publish();
            return Task.CompletedTask;
        }

        if (!running)
        {
            Enter(Phase.Idle, "stopped by operator");
            ReleaseValve();
            Publish();
            return Task.CompletedTask;
        }

        if (_dwellLeft > 0)
        {
            _dwellLeft--;
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
            ReleaseValve();
        }

        Publish();
        return Task.CompletedTask;
    }

    private void HandleCommand(string command)
    {
        switch (command.ToLowerInvariant())
        {
            case "start":
                SetGlobalVar("task.dispense.run", true);
                if (_phase is Phase.Idle or Phase.Done or Phase.Fault)
                {
                    BeginCycle("start requested");
                }
                break;
            case "stop":
                SetGlobalVar("task.dispense.run", false);
                ReleaseValve();
                Enter(Phase.Idle, "stop requested");
                break;
            case "reset":
                SetGlobalVar("task.dispense.run", false);
                _okCount = 0;
                _ngCount = 0;
                _pointIndex = 0;
                _points = [];
                DispenseLogStore.Clear();
                DispenseLogStore.Info("dispense", "reset — counters and logs cleared");
                ReleaseValve();
                Enter(Phase.Idle, "reset");
                break;
            default:
                _message = $"unknown command: {command}";
                break;
        }
    }

    private void BeginCycle(string reason)
    {
        _points = BuildPoints();
        _pointIndex = 0;
        if (_points.Count == 0)
        {
            Enter(Phase.Fault, "no dispense points");
            return;
        }

        Enter(Phase.WaitWorkpiece, $"{reason} — {_points.Count} points");
    }

    private void Step()
    {
        switch (_phase)
        {
            case Phase.WaitWorkpiece:
                if (!IsWorkpiecePresent())
                {
                    _message = "waiting for workpiece";
                    return;
                }

                Enter(Phase.Enable, "enable XYZ motion");
                break;

            case Phase.Enable:
                if (!PlatformStartMotion(_platformId))
                {
                    Enter(Phase.Fault, $"failed to enable {_platformId}");
                    return;
                }

                Enter(Phase.MoveSafeZ, "move to safe Z", DwellTicks());
                break;

            case Phase.MoveSafeZ:
                MoveZ(SafeZ());
                Enter(Phase.MoveXy, $"move XY to point #{_pointIndex + 1}", DwellTicks());
                break;

            case Phase.MoveXy:
                if (!TryCurrentPoint(out var xy))
                {
                    Enter(Phase.Fault, "point index out of range");
                    return;
                }

                MoveXy(xy.X, xy.Y);
                Enter(Phase.DescendZ, "descend to dispense Z", DwellTicks());
                break;

            case Phase.DescendZ:
                MoveZ(DispenseZ());
                Enter(Phase.ValveOn, "open valve", DwellTicks());
                break;

            case Phase.ValveOn:
                SetValve(true);
                Enter(Phase.Dwell, "dispense dwell", DwellTicks());
                break;

            case Phase.Dwell:
                Enter(Phase.ValveOff, "close valve");
                break;

            case Phase.ValveOff:
                SetValve(false);
                Enter(Phase.RetractZ, "retract to safe Z", DwellTicks());
                break;

            case Phase.RetractZ:
                MoveZ(SafeZ());
                Enter(Phase.NextPoint, "advance point");
                break;

            case Phase.NextPoint:
                _okCount++;
                _pointIndex++;
                if (_pointIndex >= _points.Count)
                {
                    Enter(Phase.Done, $"cycle done — {_okCount} dots");
                    SetGlobalVar("task.dispense.run", false);
                    return;
                }

                Enter(Phase.MoveXy, $"next point #{_pointIndex + 1}", DwellTicks());
                break;
        }
    }

    private List<(double X, double Y)> BuildPoints()
    {
        var rows = Math.Max(1, (int)GetRecipeDouble("dispense.rows", _defaultRows));
        var cols = Math.Max(1, (int)GetRecipeDouble("dispense.cols", _defaultCols));
        var ox = GetRecipeDouble("dispense.originX", _defaultOriginX);
        var oy = GetRecipeDouble("dispense.originY", _defaultOriginY);
        var px = GetRecipeDouble("dispense.pitchX", _defaultPitchX);
        var py = GetRecipeDouble("dispense.pitchY", _defaultPitchY);
        var list = new List<(double, double)>(rows * cols);
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                list.Add((ox + c * px, oy + r * py));
            }
        }

        return list;
    }

    private bool TryCurrentPoint(out (double X, double Y) point)
    {
        if (_pointIndex < 0 || _pointIndex >= _points.Count)
        {
            point = default;
            return false;
        }

        point = _points[_pointIndex];
        return true;
    }

    private void MoveXy(double x, double y)
    {
        PlatformAxisMoveTo(_platformId, "X", x);
        PlatformAxisMoveTo(_platformId, "Y", y);
        _x = x;
        _y = y;
    }

    private void MoveZ(double z)
    {
        PlatformAxisMoveTo(_platformId, "Z", z);
        _z = z;
    }

    private void SetValve(bool on)
    {
        if (!string.IsNullOrWhiteSpace(_valveAlias))
        {
            GpioWriteOutput(_gpioId, _valveAlias, on);
        }

        _valveOn = on;
    }

    private void ReleaseValve() => SetValve(false);

    private bool IsWorkpiecePresent()
    {
        if (GetFlag("task.dispense.workpiecePresent"))
        {
            return true;
        }

        if (_checkWorkpieceIo && GpioTryReadInput(_gpioId, _workpieceAlias, out var present))
        {
            return present;
        }

        // Unmapped sensor / default var: treat as present so sim can run.
        return !_checkWorkpieceIo;
    }

    private void Enter(Phase phase, string message, int dwell = 0)
    {
        var changed = phase != _phase || !string.Equals(_message, message, StringComparison.Ordinal);
        _phase = phase;
        _message = message;
        _dwellLeft = dwell;
        if (!changed)
        {
            return;
        }

        var level = phase == Phase.Fault ? "ERROR" : "INFO";
        DispenseLogStore.Add(level, "dispense", $"[{phase}] {message}");
    }

    private void Publish()
    {
        SetVar("phase", _phase.ToString());
        SetVar("message", _message);
        SetVar("okCount", _okCount);
        SetVar("ngCount", _ngCount);
        SetVar("pointIndex", _pointIndex);
        SetVar("pointTotal", _points.Count);
        SetVar("valve", _valveOn);
        SetVar("x", _x);
        SetVar("y", _y);
        SetVar("z", _z);
        SetVar("alive", true);
        SetVar("lastTickUtc", DateTime.UtcNow);
        SetGlobalVar("task.dispense.phase", _phase.ToString());
        SetGlobalVar("task.dispense.message", _message);
        SetGlobalVar("task.dispense.okCount", _okCount);
        SetGlobalVar("task.dispense.ngCount", _ngCount);
        SetGlobalVar("task.dispense.pointIndex", _pointIndex);
        SetGlobalVar("task.dispense.pointTotal", _points.Count);
        SetGlobalVar("task.dispense.valve", _valveOn);
        SetGlobalVar("task.dispense.x", _x);
        SetGlobalVar("task.dispense.y", _y);
        SetGlobalVar("task.dispense.z", _z);
        SetGlobalVar("task.dispense.rows", (int)GetRecipeDouble("dispense.rows", _defaultRows));
        SetGlobalVar("task.dispense.cols", (int)GetRecipeDouble("dispense.cols", _defaultCols));
    }

    private int DwellTicks() => Math.Max(1, (int)GetRecipeDouble("dispense.dwellTicks", _defaultDwellTicks));

    private double SafeZ() => GetRecipeDouble("dispense.safeZ", _defaultSafeZ);

    private double DispenseZ() => GetRecipeDouble("dispense.z", _defaultDispenseZ);

    private bool GetFlag(string key)
    {
        if (TryGetGlobalVar<bool>(key, out var flag))
        {
            return flag;
        }

        if (TryGetGlobalVar<string>(key, out var text)
            && bool.TryParse(text, out var parsed))
        {
            return parsed;
        }

        return false;
    }

    private double GetRecipeDouble(string key, double fallback)
    {
        if (!TryGetGlobalVar<object>(key, out var raw) || raw is null)
        {
            return fallback;
        }

        return raw switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            decimal m => (double)m,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => fallback,
        };
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

    private static bool ReadBool(IReadOnlyDictionary<string, string> parameters, string key, bool fallback)
    {
        if (!parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return bool.TryParse(raw, out var value) ? value : fallback;
    }
}
