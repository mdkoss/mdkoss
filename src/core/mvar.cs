using System.Collections.Concurrent;

namespace MDKOSS.Core;

/// <summary>
/// Thread-safe runtime variable registry shared by all modules.
/// </summary>
public sealed class MVarStore
{
    private readonly ConcurrentDictionary<string, object?> _vars = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Writes or overwrites a variable value.</summary>
    public void Set<T>(string key, T value)
    {
        _vars[key] = value;
    }

    /// <summary>Gets a value and tries type conversion when needed.</summary>
    public T? Get<T>(string key)
    {
        if (!_vars.TryGetValue(key, out var raw) || raw is null)
        {
            return default;
        }

        if (raw is T typed)
        {
            return typed;
        }

        return (T?)Convert.ChangeType(raw, typeof(T));
    }

    /// <summary>Tries to get a value with conversion support.</summary>
    public bool TryGet<T>(string key, out T? value)
    {
        value = default;
        if (!_vars.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        if (raw is T typed)
        {
            value = typed;
            return true;
        }

        value = (T?)Convert.ChangeType(raw, typeof(T));
        return true;
    }

    /// <summary>Returns a copy for safe monitoring/export.</summary>
    public IReadOnlyDictionary<string, object?> Snapshot()
    {
        return new Dictionary<string, object?>(_vars);
    }
}
