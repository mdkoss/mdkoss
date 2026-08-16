using System.Globalization;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Tasks;

namespace MDKOSS.Pnp;

/// <summary>
/// PNP cycle state machine:
/// wait trays → top-cam locate → pick → bottom-cam angle → calc place → place → advance nests.
/// </summary>
public sealed class PnpCycleTask : MotionTask
{
    private enum Phase
    {
        Idle,
        WaitTrays,
        LocateTop,
        MovePickSafe,
        MovePickXy,
        DescendPick,
        VacuumOn,
        AscendPick,
        MoveBottomCam,
        MeasureAngle,
        CalcPlace,
        MovePlaceSafe,
        MovePlaceXyU,
        DescendPlace,
        VacuumOff,
        AscendPlace,
        AdvanceNests,
        RequestTrayChange,
        Fault
    }

    private readonly string _platformId;
    private readonly string _gpioId;
    private readonly string _sourceTrayId;
    private readonly string _targetTrayId;
    private readonly string _topCameraId;
    private readonly string _bottomCameraId;
    private readonly string _vacuumAlias;
    private readonly int _dwellTicks;
    private readonly Random _rng = new(42);

    private Phase _phase = Phase.Idle;
    private int _dwellLeft;
    private double _pickX;
    private double _pickY;
    private double _pickZ;
    private double _safeZ;
    private double _placeX;
    private double _placeY;
    private double _placeZ;
    private double _placeU;
    private double _measuredAngleDeg;
    private int _okCount;
    private int _ngCount;
    private string _message = "ready";

    public PnpCycleTask(
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
        _platformId = Read(parameters, "platformDeviceId", "robot-xyz");
        _gpioId = Read(parameters, "gpioDeviceId", "gpio-pnp");
        _sourceTrayId = Read(parameters, "sourceTrayDeviceId", "tray-source");
        _targetTrayId = Read(parameters, "targetTrayDeviceId", "tray-target");
        _topCameraId = Read(parameters, "topCameraDeviceId", "cam-top");
        _bottomCameraId = Read(parameters, "bottomCameraDeviceId", "cam-bottom");
        _vacuumAlias = Read(parameters, "vacuumAlias", "vacuum");
        _dwellTicks = Math.Max(1, ReadInt(parameters, "dwellTicks", 2));

        foreach (var kv in parameters)
        {
            SetParam(kv.Key, kv.Value);
        }

        PublishStatus();
    }

    protected override Task TickAsync(CancellationToken cancellationToken)
    {
        var command = GetGlobalVar<string>("task.pnp.command");
        if (!string.IsNullOrWhiteSpace(command))
        {
            HandleCommand(command.Trim());
            SetGlobalVar("task.pnp.command", string.Empty);
        }

        var running = string.Equals(GetGlobalVar<string>("task.operation.state"), "running", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(GetGlobalVar<string>("task.pnp.run"), "1", StringComparison.OrdinalIgnoreCase)
                      || GetGlobalVar<bool>("task.pnp.run");

        if (_phase is Phase.Idle or Phase.Fault)
        {
            if (running && _phase == Phase.Idle)
            {
                Enter(Phase.WaitTrays, "waiting for source/target trays");
            }

            PublishStatus();
            return Task.CompletedTask;
        }

        if (!running && _phase is not Phase.Idle and not Phase.Fault)
        {
            Enter(Phase.Idle, "stopped by operator");
            GpioWriteOutput(_gpioId, _vacuumAlias, false);
            PublishStatus();
            return Task.CompletedTask;
        }

        if (_dwellLeft > 0)
        {
            _dwellLeft--;
            PublishStatus();
            return Task.CompletedTask;
        }

        try
        {
            Step();
        }
        catch (Exception ex)
        {
            Enter(Phase.Fault, ex.Message);
            GpioWriteOutput(_gpioId, _vacuumAlias, false);
        }

        PublishStatus();
        return Task.CompletedTask;
    }

    private void HandleCommand(string command)
    {
        switch (command.ToLowerInvariant())
        {
            case "start":
                SetGlobalVar("task.pnp.run", true);
                if (_phase is Phase.Idle or Phase.Fault)
                {
                    Enter(Phase.WaitTrays, "start requested");
                }
                break;
            case "stop":
                SetGlobalVar("task.pnp.run", false);
                Enter(Phase.Idle, "stop requested");
                GpioWriteOutput(_gpioId, _vacuumAlias, false);
                break;
            case "reset":
                SetGlobalVar("task.pnp.run", false);
                _okCount = 0;
                _ngCount = 0;
                PnpLogStore.Clear();
                PnpLogStore.Info("cycle", "reset — counters and logs cleared");
                Enter(Phase.Idle, "reset");
                GpioWriteOutput(_gpioId, _vacuumAlias, false);
                if (TryGetTray(_sourceTrayId, out var src) && src is not null)
                {
                    src.Reset();
                }

                if (TryGetTray(_targetTrayId, out var tgt) && tgt is not null)
                {
                    tgt.Reset();
                }
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
            case Phase.WaitTrays:
                if (!IsTrayPresent("task.pnp.srcTrayPresent") || !IsTrayPresent("task.pnp.tgtTrayPresent"))
                {
                    _message = "waiting tray present signals";
                    return;
                }

                if (!TryGetTray(_sourceTrayId, out var src) || src is null || src.IsExhausted
                    || !TryGetTray(_targetTrayId, out var tgt) || tgt is null || tgt.IsExhausted)
                {
                    SetGlobalVar("task.pnp.trayChangeRequest", true);
                    Enter(Phase.RequestTrayChange, "tray exhausted — request change");
                    return;
                }

                Enter(Phase.LocateTop, "top camera locate");
                break;

            case Phase.LocateTop:
                if (!TryLocateTop(out var nest))
                {
                    _ngCount++;
                    Enter(Phase.AdvanceNests, "top vision NG — skip nest");
                    return;
                }

                _pickX = nest.X + GetRecipeDouble("pnp.vision.top.offsetX", 0);
                _pickY = nest.Y + GetRecipeDouble("pnp.vision.top.offsetY", 0);
                _pickZ = nest.PickZ;
                _safeZ = nest.SafeZ;
                Enter(Phase.MovePickSafe, $"pick nest #{nest.Index} ({nest.Row},{nest.Col})");
                break;

            case Phase.MovePickSafe:
                MoveRobotZ(_safeZ);
                Enter(Phase.MovePickXy, "move XY to pick", _dwellTicks);
                break;

            case Phase.MovePickXy:
                MoveRobotXy(_pickX, _pickY);
                Enter(Phase.DescendPick, "descend to pick Z", _dwellTicks);
                break;

            case Phase.DescendPick:
                MoveRobotZ(_pickZ);
                Enter(Phase.VacuumOn, "vacuum on", _dwellTicks);
                break;

            case Phase.VacuumOn:
                GpioWriteOutput(_gpioId, _vacuumAlias, true);
                Enter(Phase.AscendPick, "ascend after pick", _dwellTicks);
                break;

            case Phase.AscendPick:
                MoveRobotZ(_safeZ);
                Enter(Phase.MoveBottomCam, "move to bottom camera", _dwellTicks);
                break;

            case Phase.MoveBottomCam:
                MoveRobotXy(
                    GetRecipeDouble("pnp.bottomCam.x", 150),
                    GetRecipeDouble("pnp.bottomCam.y", 0));
                Enter(Phase.MeasureAngle, "bottom camera measure", _dwellTicks);
                break;

            case Phase.MeasureAngle:
                if (!TryMeasureAngle(out _measuredAngleDeg))
                {
                    _ngCount++;
                    GpioWriteOutput(_gpioId, _vacuumAlias, false);
                    Enter(Phase.AdvanceNests, "bottom vision NG — discard");
                    return;
                }

                Enter(Phase.CalcPlace, $"angle={_measuredAngleDeg:F2}°");
                break;

            case Phase.CalcPlace:
                if (!TryGetTray(_targetTrayId, out var placeTray) || placeTray is null
                    || !placeTray.TryGetCurrentNest(out var placeNest))
                {
                    SetGlobalVar("task.pnp.trayChangeRequest", true);
                    Enter(Phase.RequestTrayChange, "target tray unavailable");
                    return;
                }

                // Place pose = nest + recipe offset; U compensates measured angle.
                _placeX = placeNest.X + GetRecipeDouble("pnp.place.offsetX", 0);
                _placeY = placeNest.Y + GetRecipeDouble("pnp.place.offsetY", 0);
                _placeZ = placeNest.PickZ;
                _placeU = -_measuredAngleDeg + GetRecipeDouble("pnp.place.angleOffsetDeg", 0);
                _safeZ = placeNest.SafeZ;
                SetVar("place.x", _placeX);
                SetVar("place.y", _placeY);
                SetVar("place.z", _placeZ);
                SetVar("place.u", _placeU);
                Enter(Phase.MovePlaceSafe, $"place nest #{placeNest.Index}");
                break;

            case Phase.MovePlaceSafe:
                MoveRobotZ(_safeZ);
                Enter(Phase.MovePlaceXyU, "move XYU to place", _dwellTicks);
                break;

            case Phase.MovePlaceXyU:
                MoveRobotXy(_placeX, _placeY);
                MoveRobotU(_placeU);
                Enter(Phase.DescendPlace, "descend to place Z", _dwellTicks);
                break;

            case Phase.DescendPlace:
                MoveRobotZ(_placeZ);
                Enter(Phase.VacuumOff, "vacuum off", _dwellTicks);
                break;

            case Phase.VacuumOff:
                GpioWriteOutput(_gpioId, _vacuumAlias, false);
                Enter(Phase.AscendPlace, "ascend after place", _dwellTicks);
                break;

            case Phase.AscendPlace:
                MoveRobotZ(_safeZ);
                _okCount++;
                Enter(Phase.AdvanceNests, "place OK");
                break;

            case Phase.AdvanceNests:
                if (TryGetTray(_sourceTrayId, out var s) && s is not null)
                {
                    s.Advance();
                }

                if (TryGetTray(_targetTrayId, out var t) && t is not null)
                {
                    t.Advance();
                }

                Enter(Phase.WaitTrays, "next cycle");
                break;

            case Phase.RequestTrayChange:
                // Conveyor task clears trayChangeRequest after trays are swapped.
                if (!GetGlobalVar<bool>("task.pnp.trayChangeRequest"))
                {
                    Enter(Phase.WaitTrays, "tray change completed");
                }
                else
                {
                    _message = "waiting tray change";
                }
                break;
        }
    }

    private bool IsTrayPresent(string varKey)
    {
        if (TryGetGlobalVar<bool>(varKey, out var flag))
        {
            return flag;
        }

        if (TryGetGlobalVar<string>(varKey, out var text)
            && bool.TryParse(text, out var parsed))
        {
            return parsed;
        }

        // Default present for first boot when var not seeded.
        return true;
    }

    private bool TryLocateTop(out TrayNestPose nest)
    {
        nest = default;
        if (!TryGetTray(_sourceTrayId, out var tray) || tray is null || !tray.TryGetCurrentNest(out nest))
        {
            return false;
        }

        if (TryGetDevice<CameraDevDevice>(_topCameraId, out var cam) && cam is not null)
        {
            cam.TriggerCapture(GetRecipeString("pnp.vision.top.recipe", "top-locate"));
        }

        // Simulated vision result around nest center.
        var dx = (_rng.NextDouble() - 0.5) * GetRecipeDouble("pnp.vision.top.noiseMm", 0.4);
        var dy = (_rng.NextDouble() - 0.5) * GetRecipeDouble("pnp.vision.top.noiseMm", 0.4);
        var ok = _rng.NextDouble() > GetRecipeDouble("pnp.vision.top.ngRate", 0.02);
        SetGlobalVar("pnp.vision.top.x", nest.X + dx);
        SetGlobalVar("pnp.vision.top.y", nest.Y + dy);
        SetGlobalVar("pnp.vision.top.ok", ok);
        SetVar("vision.top.x", nest.X + dx);
        SetVar("vision.top.y", nest.Y + dy);
        SetVar("vision.top.ok", ok);
        if (!ok)
        {
            return false;
        }

        nest = nest with { X = nest.X + dx, Y = nest.Y + dy };
        return true;
    }

    private bool TryMeasureAngle(out double angleDeg)
    {
        if (TryGetDevice<CameraDevDevice>(_bottomCameraId, out var cam) && cam is not null)
        {
            cam.TriggerCapture(GetRecipeString("pnp.vision.bottom.recipe", "bottom-angle"));
        }

        angleDeg = (_rng.NextDouble() - 0.5) * GetRecipeDouble("pnp.vision.bottom.angleRangeDeg", 20);
        var ok = _rng.NextDouble() > GetRecipeDouble("pnp.vision.bottom.ngRate", 0.02);
        SetGlobalVar("pnp.vision.bottom.angleDeg", angleDeg);
        SetGlobalVar("pnp.vision.bottom.ok", ok);
        SetVar("vision.bottom.angleDeg", angleDeg);
        SetVar("vision.bottom.ok", ok);
        return ok;
    }

    private void MoveRobotXy(double x, double y)
    {
        PlatformAxisMoveTo(_platformId, "X", x);
        PlatformAxisMoveTo(_platformId, "Y", y);
        SetVar("robot.x", x);
        SetVar("robot.y", y);
    }

    private void MoveRobotZ(double z)
    {
        PlatformAxisMoveTo(_platformId, "Z", z);
        SetVar("robot.z", z);
    }

    private void MoveRobotU(double u)
    {
        PlatformAxisMoveTo(_platformId, "U", u);
        SetVar("robot.u", u);
    }

    private bool TryGetTray(string id, out TrayDevice? tray) => TryGetDevice(id, out tray);

    private void Enter(Phase phase, string message, int dwell = 0)
    {
        var changed = phase != _phase || !string.Equals(_message, message, StringComparison.Ordinal);
        _phase = phase;
        _message = message;
        _dwellLeft = dwell;
        SetVar("phase", phase.ToString());
        SetVar("message", message);
        if (!changed)
        {
            return;
        }

        var level = phase == Phase.Fault ? "ERROR" : "INFO";
        PnpLogStore.Add(level, "cycle", $"[{phase}] {message}");
    }

    private void PublishStatus()
    {
        SetVar("phase", _phase.ToString());
        SetVar("message", _message);
        SetVar("okCount", _okCount);
        SetVar("ngCount", _ngCount);
        SetVar("alive", true);
        SetVar("lastTickUtc", DateTime.UtcNow);
        SetGlobalVar("task.pnp.phase", _phase.ToString());
        SetGlobalVar("task.pnp.message", _message);
        SetGlobalVar("task.pnp.okCount", _okCount);
        SetGlobalVar("task.pnp.ngCount", _ngCount);
    }

    private double GetRecipeDouble(string key, double fallback)
    {
        try
        {
            if (TryGetGlobalVar<object>(key, out var raw) && raw is not null)
            {
                switch (raw)
                {
                    case double d:
                        return d;
                    case float f:
                        return f;
                    case int i:
                        return i;
                    case long l:
                        return l;
                    case decimal m:
                        return (double)m;
                    case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                        return parsed;
                    case System.Text.Json.JsonElement je:
                        if (je.ValueKind == System.Text.Json.JsonValueKind.Number && je.TryGetDouble(out var jd))
                        {
                            return jd;
                        }

                        if (je.ValueKind == System.Text.Json.JsonValueKind.String
                            && double.TryParse(je.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var js))
                        {
                            return js;
                        }
                        break;
                }
            }
        }
        catch
        {
            // Fall through to string / fallback paths.
        }

        if (TryGetGlobalVar<string>(key, out var text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var fromText))
        {
            return fromText;
        }

        return fallback;
    }

    private string GetRecipeString(string key, string fallback)
    {
        if (TryGetGlobalVar<string>(key, out var s) && !string.IsNullOrWhiteSpace(s))
        {
            return s!;
        }

        return fallback;
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
