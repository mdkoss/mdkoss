using System.Globalization;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Pnp;
using MDKOSS.Tasks;

namespace MDKOSS.Sample.DieBonder;

/// <summary>
/// 半导体贴片主循环：
/// 等盘 → 下视定位 → 顶针+拾取 → 上视测角 → 点胶(可选) → 贴装 → 推进 nest。
/// 状态变量沿用 <c>task.pnp.*</c>，以兼容 PNP 监控页 / API。
/// </summary>
public sealed class BondCycleTask : MotionTask
{
    private enum Phase
    {
        Idle,
        WaitTrays,
        CheckSafety,
        LocateDownlook,
        MovePickSafe,
        MovePickXy,
        EjectorOn,
        DescendPick,
        VacuumOn,
        AscendPick,
        EjectorOff,
        MoveUplook,
        MeasureAngle,
        CalcPlace,
        MovePlaceSafe,
        MovePlaceXyU,
        Dispense,
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
    private readonly string _downlookCameraId;
    private readonly string _uplookCameraId;
    private readonly string _vacuumAlias;
    private readonly string _ejectorAlias;
    private readonly string _dispenserAlias;
    private readonly string _safetyDoorAlias;
    private readonly bool _useEjector;
    private readonly bool _useDispenser;
    private readonly bool _checkSafetyDoor;
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

    public BondCycleTask(
        string name,
        int intervalMs,
        IDriver driver,
        MVarStore vars,
        IReadOnlyDictionary<string, MDeviceBase> devices,
        IReadOnlyDictionary<string, string>? parameters = null)
        : base(name, intervalMs, driver, vars, devices)
    {
        parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _platformId = Read(parameters, "platformDeviceId", "head-bond");
        _gpioId = Read(parameters, "gpioDeviceId", "gpio-machine");
        _sourceTrayId = Read(parameters, "sourceTrayDeviceId", "tray-wafer");
        _targetTrayId = Read(parameters, "targetTrayDeviceId", "tray-substrate");
        _downlookCameraId = Read(parameters, "topCameraDeviceId",
            Read(parameters, "downlookCameraDeviceId", "cam-downlook"));
        _uplookCameraId = Read(parameters, "bottomCameraDeviceId",
            Read(parameters, "uplookCameraDeviceId", "cam-uplook"));
        _vacuumAlias = Read(parameters, "vacuumAlias", "vacuum");
        _ejectorAlias = Read(parameters, "ejectorAlias", "ejector");
        _dispenserAlias = Read(parameters, "dispenserAlias", "dispenser");
        _safetyDoorAlias = Read(parameters, "safetyDoorAlias", "safetyDoor");
        _useEjector = ReadBool(parameters, "useEjector", true);
        _useDispenser = ReadBool(parameters, "useDispenser", false);
        _checkSafetyDoor = ReadBool(parameters, "checkSafetyDoor", false);
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
                Enter(Phase.WaitTrays, "waiting for wafer/substrate");
            }

            PublishStatus();
            return Task.CompletedTask;
        }

        if (!running && _phase is not Phase.Idle and not Phase.Fault)
        {
            Enter(Phase.Idle, "stopped by operator");
            ReleaseActuators();
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
            ReleaseActuators();
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
                ReleaseActuators();
                break;
            case "reset":
                SetGlobalVar("task.pnp.run", false);
                _okCount = 0;
                _ngCount = 0;
                BondLogStore.Clear();
                BondLogStore.Info("bond", "reset — counters and logs cleared");
                Enter(Phase.Idle, "reset");
                ReleaseActuators();
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
                    _message = "waiting wafer/substrate present";
                    return;
                }

                if (!TryGetTray(_sourceTrayId, out var src) || src is null || src.IsExhausted
                    || !TryGetTray(_targetTrayId, out var tgt) || tgt is null || tgt.IsExhausted)
                {
                    SetGlobalVar("task.pnp.trayChangeRequest", true);
                    Enter(Phase.RequestTrayChange, "tray exhausted — request change");
                    return;
                }

                Enter(Phase.CheckSafety, "check safety door");
                break;

            case Phase.CheckSafety:
                if (_checkSafetyDoor && IsSafetyDoorOpen())
                {
                    Enter(Phase.Fault, "safety door open");
                    return;
                }

                Enter(Phase.LocateDownlook, "downlook locate die");
                break;

            case Phase.LocateDownlook:
                if (!TryLocateDownlook(out var nest))
                {
                    _ngCount++;
                    Enter(Phase.AdvanceNests, "downlook NG — skip nest");
                    return;
                }

                _pickX = nest.X + GetRecipeDouble("pnp.vision.top.offsetX", 0);
                _pickY = nest.Y + GetRecipeDouble("pnp.vision.top.offsetY", 0);
                _pickZ = nest.PickZ;
                _safeZ = nest.SafeZ;
                Enter(Phase.MovePickSafe, $"pick nest #{nest.Index} ({nest.Row},{nest.Col})");
                break;

            case Phase.MovePickSafe:
                MoveHeadZ(_safeZ);
                Enter(Phase.MovePickXy, "move XY to pick", _dwellTicks);
                break;

            case Phase.MovePickXy:
                MoveHeadXy(_pickX, _pickY);
                Enter(_useEjector ? Phase.EjectorOn : Phase.DescendPick,
                    _useEjector ? "ejector on" : "descend to pick Z",
                    _dwellTicks);
                break;

            case Phase.EjectorOn:
                GpioWriteOutput(_gpioId, _ejectorAlias, true);
                Enter(Phase.DescendPick, "descend to pick Z", _dwellTicks);
                break;

            case Phase.DescendPick:
                MoveHeadZ(_pickZ);
                Enter(Phase.VacuumOn, "vacuum on", _dwellTicks);
                break;

            case Phase.VacuumOn:
                GpioWriteOutput(_gpioId, _vacuumAlias, true);
                Enter(Phase.AscendPick, "ascend after pick", _dwellTicks);
                break;

            case Phase.AscendPick:
                MoveHeadZ(_safeZ);
                Enter(_useEjector ? Phase.EjectorOff : Phase.MoveUplook,
                    _useEjector ? "ejector off" : "move to uplook",
                    _dwellTicks);
                break;

            case Phase.EjectorOff:
                GpioWriteOutput(_gpioId, _ejectorAlias, false);
                Enter(Phase.MoveUplook, "move to uplook", _dwellTicks);
                break;

            case Phase.MoveUplook:
                MoveHeadXy(
                    GetRecipeDouble("pnp.bottomCam.x", 180),
                    GetRecipeDouble("pnp.bottomCam.y", 100));
                Enter(Phase.MeasureAngle, "uplook measure angle", _dwellTicks);
                break;

            case Phase.MeasureAngle:
                if (!TryMeasureAngle(out _measuredAngleDeg))
                {
                    _ngCount++;
                    GpioWriteOutput(_gpioId, _vacuumAlias, false);
                    Enter(Phase.AdvanceNests, "uplook NG — discard die");
                    return;
                }

                Enter(Phase.CalcPlace, $"angle={_measuredAngleDeg:F2}°");
                break;

            case Phase.CalcPlace:
                if (!TryGetTray(_targetTrayId, out var placeTray) || placeTray is null
                    || !placeTray.TryGetCurrentNest(out var placeNest))
                {
                    SetGlobalVar("task.pnp.trayChangeRequest", true);
                    Enter(Phase.RequestTrayChange, "substrate unavailable");
                    return;
                }

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
                MoveHeadZ(_safeZ);
                Enter(Phase.MovePlaceXyU, "move XYU to place", _dwellTicks);
                break;

            case Phase.MovePlaceXyU:
                MoveHeadXy(_placeX, _placeY);
                MoveHeadU(_placeU);
                Enter(_useDispenser ? Phase.Dispense : Phase.DescendPlace,
                    _useDispenser ? "dispense epoxy" : "descend to place Z",
                    _dwellTicks);
                break;

            case Phase.Dispense:
                GpioWriteOutput(_gpioId, _dispenserAlias, true);
                // Short pulse then off before bond.
                GpioWriteOutput(_gpioId, _dispenserAlias, false);
                Enter(Phase.DescendPlace, "descend to place Z", _dwellTicks);
                break;

            case Phase.DescendPlace:
                MoveHeadZ(_placeZ);
                Enter(Phase.VacuumOff, "vacuum off", _dwellTicks);
                break;

            case Phase.VacuumOff:
                GpioWriteOutput(_gpioId, _vacuumAlias, false);
                Enter(Phase.AscendPlace, "ascend after place", _dwellTicks);
                break;

            case Phase.AscendPlace:
                MoveHeadZ(_safeZ);
                _okCount++;
                Enter(Phase.AdvanceNests, "bond OK");
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

    private void ReleaseActuators()
    {
        GpioWriteOutput(_gpioId, _vacuumAlias, false);
        if (_useEjector)
        {
            GpioWriteOutput(_gpioId, _ejectorAlias, false);
        }

        if (_useDispenser)
        {
            GpioWriteOutput(_gpioId, _dispenserAlias, false);
        }
    }

    private bool IsSafetyDoorOpen()
    {
        // Convention: input true = door closed/safe. Open door → not safe.
        if (!GpioTryReadInput(_gpioId, _safetyDoorAlias, out var closedOrSafe))
        {
            // Unmapped sensor: treat as safe for sim.
            return false;
        }

        return !closedOrSafe;
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

        return true;
    }

    private bool TryLocateDownlook(out TrayNestPose nest)
    {
        nest = default;
        if (!TryGetTray(_sourceTrayId, out var tray) || tray is null || !tray.TryGetCurrentNest(out nest))
        {
            return false;
        }

        if (TryGetDevice<CameraDevDevice>(_downlookCameraId, out var cam) && cam is not null)
        {
            cam.TriggerCapture(GetRecipeString("pnp.vision.top.recipe", "wafer-die-locate"));
        }

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
        if (TryGetDevice<CameraDevDevice>(_uplookCameraId, out var cam) && cam is not null)
        {
            cam.TriggerCapture(GetRecipeString("pnp.vision.bottom.recipe", "die-angle"));
        }

        angleDeg = (_rng.NextDouble() - 0.5) * GetRecipeDouble("pnp.vision.bottom.angleRangeDeg", 20);
        var ok = _rng.NextDouble() > GetRecipeDouble("pnp.vision.bottom.ngRate", 0.02);
        SetGlobalVar("pnp.vision.bottom.angleDeg", angleDeg);
        SetGlobalVar("pnp.vision.bottom.ok", ok);
        SetVar("vision.bottom.angleDeg", angleDeg);
        SetVar("vision.bottom.ok", ok);
        return ok;
    }

    private void MoveHeadXy(double x, double y)
    {
        PlatformAxisMoveTo(_platformId, "X", x);
        PlatformAxisMoveTo(_platformId, "Y", y);
        SetVar("robot.x", x);
        SetVar("robot.y", y);
    }

    private void MoveHeadZ(double z)
    {
        PlatformAxisMoveTo(_platformId, "Z", z);
        SetVar("robot.z", z);
    }

    private void MoveHeadU(double u)
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
        BondLogStore.Add(level, "bond", $"[{phase}] {message}");
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
        SetGlobalVar("task.bond.phase", _phase.ToString());
        SetGlobalVar("task.bond.message", _message);
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
            // Fall through.
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

    private static bool ReadBool(IReadOnlyDictionary<string, string> parameters, string key, bool fallback)
    {
        if (!parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return bool.TryParse(raw, out var value) ? value : fallback;
    }
}
