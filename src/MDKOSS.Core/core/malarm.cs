using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MDKOSS.Core;

/// <summary>
/// Runtime alarm manager: catalogs definitions from <see cref="MdkSetting.Alarms"/>
/// and tracks currently active alarms for monitoring / task APIs.
/// </summary>
public sealed class MdkAlarmManager
{
    /// <summary>Var key holding the active-alarm list snapshot (JSON-friendly objects).</summary>
    public const string ActiveVarKey = "alarms.active";

    /// <summary>Var key holding the count of active alarms.</summary>
    public const string CountVarKey = "alarms.count";

    /// <summary>Builds per-alarm latch var key: <c>alarms.{alarmKey}</c> (1 = active, 0 = cleared).</summary>
    public static string FlagVarKey(string alarmKey) => $"alarms.{alarmKey.Trim()}";

    private static readonly JsonSerializerOptions PublishJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly MdkSetting _setting;
    private readonly MVarStore _vars;
    private readonly ConcurrentDictionary<string, MdkSetting.AlarmConfig> _active =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public MdkAlarmManager(MdkSetting setting, MVarStore vars)
    {
        _setting = setting ?? throw new ArgumentNullException(nameof(setting));
        _vars = vars ?? throw new ArgumentNullException(nameof(vars));
        Publish();
    }

    /// <summary>Currently active alarms (snapshot copy).</summary>
    public IReadOnlyList<MdkSetting.AlarmConfig> GetActive()
    {
        return _active.Values
            .OrderByDescending(a => a.TriggerTime, StringComparer.Ordinal)
            .Select(Clone)
            .ToList();
    }

    /// <summary>Looks up a catalog definition by key or id.</summary>
    public bool TryGetDefinition(string key, out MdkSetting.AlarmConfig? definition)
    {
        definition = null;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var trimmed = key.Trim();
        definition = _setting.Alarms.FirstOrDefault(a =>
            string.Equals(a.EffectiveId, trimmed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.Key, trimmed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.Id, trimmed, StringComparison.OrdinalIgnoreCase));
        return definition is not null;
    }

    /// <summary>
    /// Triggers an alarm by catalog key. Sets <see cref="MdkSetting.AlarmConfig.TriggerTime"/>
    /// and marks it active. Unknown keys fail unless <paramref name="allowAdHoc"/> is true.
    /// </summary>
    public bool Trigger(
        string key,
        out string? error,
        string? msgOverride = null,
        string? codeOverride = null,
        string? solutionOverride = null,
        string? moduleOverride = null,
        bool? displayOverride = null,
        bool allowAdHoc = false)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(key))
        {
            error = "alarm_key_required";
            return false;
        }

        var trimmed = key.Trim();
        TryGetDefinition(trimmed, out var def);
        if (def is null && !allowAdHoc)
        {
            error = "alarm_not_found";
            return false;
        }

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var active = new MdkSetting.AlarmConfig
        {
            Key = trimmed,
            Id = def?.EffectiveId ?? trimmed,
            Name = def?.Name ?? "",
            Msg = FirstNonEmpty(msgOverride, def?.EffectiveMessage) ?? trimmed,
            Message = FirstNonEmpty(msgOverride, def?.EffectiveMessage) ?? trimmed,
            Code = FirstNonEmpty(codeOverride, def?.Code) ?? "",
            Solution = FirstNonEmpty(solutionOverride, def?.Solution) ?? "",
            TriggerTime = now,
            Module = FirstNonEmpty(moduleOverride, def?.Module) ?? "",
            Display = displayOverride ?? def?.Display ?? true,
            Level = def?.Level ?? "error",
        };

        lock (_gate)
        {
            _active[trimmed] = active;
            if (def is not null)
            {
                def.TriggerTime = now;
            }

            _vars.Set(FlagVarKey(trimmed), 1);
            Publish();
        }

        return true;
    }

    /// <summary>Clears an active alarm by key. Returns false when not active.</summary>
    public bool Clear(string key, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(key))
        {
            error = "alarm_key_required";
            return false;
        }

        var trimmed = key.Trim();
        lock (_gate)
        {
            var removed = _active.TryRemove(trimmed, out _);
            if (!removed)
            {
                var match = _active.Keys.FirstOrDefault(k =>
                    string.Equals(k, trimmed, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    removed = _active.TryRemove(match, out _);
                    trimmed = match;
                }
            }

            if (!removed)
            {
                error = "alarm_not_active";
                return false;
            }

            if (TryGetDefinition(trimmed, out var def) && def is not null)
            {
                def.TriggerTime = string.Empty;
            }

            _vars.Set(FlagVarKey(trimmed), 0);
            Publish();
        }

        return true;
    }

    /// <summary>Clears every active alarm.</summary>
    public void ClearAll()
    {
        lock (_gate)
        {
            foreach (var key in _active.Keys.ToList())
            {
                _vars.Set(FlagVarKey(key), 0);
            }

            _active.Clear();
            foreach (var def in _setting.Alarms)
            {
                def.TriggerTime = string.Empty;
            }

            Publish();
        }
    }

    private void Publish()
    {
        var list = _active.Values
            .OrderByDescending(a => a.TriggerTime, StringComparer.Ordinal)
            .Select(a => new
            {
                key = a.EffectiveId,
                msg = a.EffectiveMessage,
                code = a.Code,
                solution = a.Solution,
                triggertime = a.TriggerTime,
                module = a.Module,
                display = a.Display,
            })
            .ToList();

        _vars.Set(CountVarKey, list.Count);
        _vars.Set(ActiveVarKey, JsonSerializer.Serialize(list, PublishJsonOptions));
    }

    private static MdkSetting.AlarmConfig Clone(MdkSetting.AlarmConfig src) => new()
    {
        Key = src.Key,
        Id = src.Id,
        Name = src.Name,
        Msg = src.Msg,
        Message = src.Message,
        Code = src.Code,
        Solution = src.Solution,
        TriggerTime = src.TriggerTime,
        Module = src.Module,
        Display = src.Display,
        Level = src.Level,
        Enabled = src.Enabled,
        VarKey = src.VarKey,
        Op = src.Op,
        Value = src.Value,
        Latch = src.Latch,
    };

    private static string? FirstNonEmpty(string? preferred, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback.Trim();
        }

        return null;
    }
}

/// <summary>One evaluated alarm row for monitoring / HMI.</summary>
public sealed class AlarmItem
{
    public string Id { get; init; } = "";
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Level { get; init; } = "error";
    public string Source { get; init; } = "config";
    public string Message { get; init; } = "";
    public string? VarKey { get; init; }
    public string? Value { get; init; }
    public bool Active { get; init; }
    public bool Acked { get; init; }
    public bool Latched { get; init; }
    public DateTimeOffset? FirstActiveUtc { get; init; }
    public DateTimeOffset? LastActiveUtc { get; init; }
}

/// <summary>
/// Evaluates configured <see cref="MdkSetting.Alarms"/> plus implicit runtime faults
/// and catalog alarms raised via <see cref="MdkAlarmManager"/>.
/// </summary>
public sealed class MdkAlarmHub
{
    private readonly object _sync = new();
    private readonly Dictionary<string, AlarmRuntimeState> _states = new(StringComparer.OrdinalIgnoreCase);

    private sealed class AlarmRuntimeState
    {
        public bool Latched;
        public bool Acked;
        public DateTimeOffset? FirstActiveUtc;
        public DateTimeOffset? LastActiveUtc;
        public string LastMessage = "";
    }

    public IReadOnlyList<AlarmItem> Evaluate(MdkRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var snap = runtime.GetSnapshot();
        var now = DateTimeOffset.UtcNow;
        var items = new List<AlarmItem>();

        lock (_sync)
        {
            foreach (var def in runtime.Setting.Alarms ?? [])
            {
                var id = def.EffectiveId;
                if (string.IsNullOrWhiteSpace(id) || !def.Enabled)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(def.VarKey))
                {
                    continue;
                }

                snap.Vars.TryGetValue(def.VarKey, out var raw);
                var condition = Match(raw, def.Op, def.Value);
                var message = string.IsNullOrWhiteSpace(def.EffectiveMessage)
                    ? $"{def.Name}: {FormatValue(raw)}"
                    : def.EffectiveMessage;
                items.Add(BuildItem(
                    id,
                    string.IsNullOrWhiteSpace(def.Code) ? id : def.Code,
                    string.IsNullOrWhiteSpace(def.Name) ? id : def.Name,
                    NormalizeLevel(def.Level),
                    "config",
                    message,
                    def.VarKey,
                    FormatValue(raw),
                    condition,
                    def.Latch,
                    now));
            }

            AddImplicit(items, snap, now);
            AddCatalogActive(items, runtime, now);
            PublishCounts(runtime, items);
        }

        return items
            .OrderBy(a => LevelRank(a.Level))
            .ThenByDescending(a => a.Active)
            .ThenBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool TryAck(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        lock (_sync)
        {
            if (!_states.TryGetValue(id.Trim(), out var st))
            {
                st = new AlarmRuntimeState();
                _states[id.Trim()] = st;
            }

            st.Acked = true;
            return true;
        }
    }

    public int AckAll()
    {
        lock (_sync)
        {
            var n = 0;
            foreach (var st in _states.Values)
            {
                if (!st.Acked)
                {
                    st.Acked = true;
                    n++;
                }
            }

            return n;
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _states.Clear();
        }
    }

    private void AddCatalogActive(List<AlarmItem> items, MdkRuntime runtime, DateTimeOffset now)
    {
        var known = new HashSet<string>(items.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var a in runtime.AlarmManager.GetActive())
        {
            if (!a.Display)
            {
                continue;
            }

            var id = a.EffectiveId;
            if (string.IsNullOrWhiteSpace(id) || known.Contains(id))
            {
                continue;
            }

            items.Add(BuildItem(
                id,
                string.IsNullOrWhiteSpace(a.Code) ? id : a.Code,
                string.IsNullOrWhiteSpace(a.Name) ? id : a.Name,
                NormalizeLevel(a.Level),
                "catalog",
                a.EffectiveMessage,
                null,
                a.TriggerTime,
                condition: true,
                latch: false,
                now));
            known.Add(id);
        }
    }

    private AlarmItem BuildItem(
        string id,
        string code,
        string name,
        string level,
        string source,
        string message,
        string? varKey,
        string? value,
        bool condition,
        bool latch,
        DateTimeOffset now)
    {
        if (!_states.TryGetValue(id, out var st))
        {
            st = new AlarmRuntimeState();
            _states[id] = st;
        }

        if (condition)
        {
            st.FirstActiveUtc ??= now;
            st.LastActiveUtc = now;
            st.LastMessage = message;
            if (latch)
            {
                st.Latched = true;
            }
        }
        else if (st.Acked || !latch)
        {
            st.Latched = false;
            if (!latch)
            {
                st.Acked = false;
                st.FirstActiveUtc = null;
            }
        }

        var active = condition || (latch && st.Latched && !st.Acked);
        if (!active && st.Acked && !condition)
        {
            st.Latched = false;
            st.FirstActiveUtc = null;
        }

        return new AlarmItem
        {
            Id = id,
            Code = code,
            Name = name,
            Level = level,
            Source = source,
            Message = string.IsNullOrWhiteSpace(st.LastMessage) ? message : st.LastMessage,
            VarKey = varKey,
            Value = value,
            Active = active,
            Acked = st.Acked,
            Latched = st.Latched,
            FirstActiveUtc = st.FirstActiveUtc,
            LastActiveUtc = st.LastActiveUtc,
        };
    }

    private void AddImplicit(List<AlarmItem> items, RuntimeSnapshot snap, DateTimeOffset now)
    {
        var known = new HashSet<string>(items.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);

        void Add(string id, string code, string name, string level, string message, string? varKey, string? value, bool condition)
        {
            if (known.Contains(id))
            {
                return;
            }

            items.Add(BuildItem(id, code, name, level, "runtime", message, varKey, value, condition, latch: false, now));
        }

        snap.Vars.TryGetValue("machine.state", out var machineState);
        var machine = FormatValue(machineState);
        snap.Vars.TryGetValue("machine.message", out var machineMsg);
        if (string.Equals(machine, "fault", StringComparison.OrdinalIgnoreCase))
        {
            Add(
                "runtime.machine",
                "RT-MACH",
                "整机故障",
                "error",
                string.IsNullOrWhiteSpace(FormatValue(machineMsg)) ? "machine.state=fault" : FormatValue(machineMsg)!,
                "machine.state",
                machine,
                true);
        }

        snap.Vars.TryGetValue("task.operation.state", out var opState);
        var op = FormatValue(opState);
        snap.Vars.TryGetValue("task.operation.message", out var opMsg);
        if (string.Equals(op, "fault", StringComparison.OrdinalIgnoreCase))
        {
            Add(
                "runtime.task.operation",
                "RT-OP",
                "操作任务故障",
                "error",
                string.IsNullOrWhiteSpace(FormatValue(opMsg)) ? "task.operation.state=fault" : FormatValue(opMsg)!,
                "task.operation.state",
                op,
                true);
        }

        if (TryNumber(snap.Vars, "task.cycle.dev.fault", out var df) && df > 0)
        {
            Add("runtime.task.cycle.dev", "RT-DEV", "设备故障计数", "error", $"设备故障 {df} 台", "task.cycle.dev.fault", df.ToString(CultureInfo.InvariantCulture), true);
        }

        if (TryNumber(snap.Vars, "task.cycle.task.fault", out var tf) && tf > 0)
        {
            Add("runtime.task.cycle.task", "RT-TASK", "任务故障计数", "warn", $"任务故障 {tf} 个", "task.cycle.task.fault", tf.ToString(CultureInfo.InvariantCulture), true);
        }

        foreach (var (id, d) in snap.Devices)
        {
            var st = (d.State ?? "").Trim().ToLowerInvariant();
            if (st is "fault" or "error")
            {
                Add(
                    "runtime.device." + id,
                    "RT-DEV",
                    $"设备 {d.Name ?? id}",
                    "error",
                    $"设备 {id} 状态 {st}",
                    null,
                    st,
                    true);
            }
        }
    }

    private static void PublishCounts(MdkRuntime runtime, IReadOnlyList<AlarmItem> items)
    {
        var active = items.Count(a => a.Active);
        var errors = items.Count(a => a.Active && string.Equals(a.Level, "error", StringComparison.OrdinalIgnoreCase));
        var warns = items.Count(a => a.Active && string.Equals(a.Level, "warn", StringComparison.OrdinalIgnoreCase));
        var unacked = items.Count(a => a.Active && !a.Acked);
        runtime.Vars.Set("alarm.activeCount", active);
        runtime.Vars.Set("alarm.errorCount", errors);
        runtime.Vars.Set("alarm.warnCount", warns);
        runtime.Vars.Set("alarm.unackedCount", unacked);
    }

    public static bool Match(object? actual, string? op, string? expected)
    {
        var a = FormatValue(actual) ?? "";
        var b = expected ?? "";
        var kind = (op ?? "eq").Trim().ToLowerInvariant();

        bool nums(out double na, out double nb)
        {
            var okA = double.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out na);
            var okB = double.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out nb);
            return okA && okB;
        }

        switch (kind)
        {
            case "eq" or "equals" or "==":
                if (nums(out var ea, out var eb))
                {
                    return Math.Abs(ea - eb) < 1e-9;
                }

                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            case "ne" or "!=" or "<>":
                return !Match(actual, "eq", expected);
            case "gt" or ">":
                return nums(out var ga, out var gb) && ga > gb;
            case "lt" or "<":
                return nums(out var la, out var lb) && la < lb;
            case "ge" or ">=":
                return nums(out var gea, out var geb) && gea >= geb;
            case "le" or "<=":
                return nums(out var lea, out var leb) && lea <= leb;
            case "empty":
                return string.IsNullOrWhiteSpace(a);
            case "nonempty":
                return !string.IsNullOrWhiteSpace(a);
            case "truthy":
                return IsTruthy(a);
            case "falsy":
                return !IsTruthy(a);
            default:
                return false;
        }
    }

    private static bool IsTruthy(string a)
    {
        var t = a.Trim().ToLowerInvariant();
        return t is "1" or "true" or "yes" or "y" or "on";
    }

    private static bool TryNumber(IReadOnlyDictionary<string, object?> vars, string key, out double value)
    {
        value = 0;
        if (!vars.TryGetValue(key, out var raw))
        {
            return false;
        }

        return double.TryParse(FormatValue(raw), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    private static string? FormatValue(object? raw)
    {
        if (raw is null)
        {
            return null;
        }

        return raw switch
        {
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => raw.ToString(),
        };
    }

    private static string NormalizeLevel(string? level)
    {
        var t = (level ?? "error").Trim().ToLowerInvariant();
        return t switch
        {
            "warn" or "warning" => "warn",
            "info" or "information" => "info",
            _ => "error",
        };
    }

    private static int LevelRank(string level) =>
        level.ToLowerInvariant() switch
        {
            "error" => 0,
            "warn" => 1,
            _ => 2,
        };
}
