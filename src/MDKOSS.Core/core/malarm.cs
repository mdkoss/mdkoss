using System.Collections.Concurrent;
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

    /// <summary>Looks up a catalog definition by key.</summary>
    public bool TryGetDefinition(string key, out MdkSetting.AlarmConfig? definition)
    {
        definition = null;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        definition = _setting.Alarms.FirstOrDefault(a =>
            string.Equals(a.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
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
            Msg = FirstNonEmpty(msgOverride, def?.Msg) ?? trimmed,
            Code = FirstNonEmpty(codeOverride, def?.Code) ?? "",
            Solution = FirstNonEmpty(solutionOverride, def?.Solution) ?? "",
            TriggerTime = now,
            Module = FirstNonEmpty(moduleOverride, def?.Module) ?? "",
            Display = displayOverride ?? def?.Display ?? true,
        };

        lock (_gate)
        {
            _active[trimmed] = active;
            if (def is not null)
            {
                def.TriggerTime = now;
            }

            // Latch bit in MVarStore: alarms.{key} = 1
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
            if (!_active.TryRemove(trimmed, out _))
            {
                error = "alarm_not_active";
                return false;
            }

            if (TryGetDefinition(trimmed, out var def) && def is not null)
            {
                def.TriggerTime = string.Empty;
            }

            // Latch bit in MVarStore: alarms.{key} = 0
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
                key = a.Key,
                msg = a.Msg,
                code = a.Code,
                solution = a.Solution,
                triggertime = a.TriggerTime,
                module = a.Module,
                display = a.Display,
            })
            .ToList();

        _vars.Set(CountVarKey, list.Count);
        // Store as JSON string so monitoring pages can parse reliably.
        _vars.Set(ActiveVarKey, JsonSerializer.Serialize(list, PublishJsonOptions));
    }

    private static MdkSetting.AlarmConfig Clone(MdkSetting.AlarmConfig src) => new()
    {
        Key = src.Key,
        Msg = src.Msg,
        Code = src.Code,
        Solution = src.Solution,
        TriggerTime = src.TriggerTime,
        Module = src.Module,
        Display = src.Display,
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
