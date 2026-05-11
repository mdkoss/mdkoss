using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace MDKOSS.Core;

/// <summary>
/// Thread-safe runtime variable registry shared by all modules.
/// Uses striped <see cref="ConcurrentDictionary{TKey,TValue}"/> shards to cut lock contention under concurrent writers.
/// </summary>
public sealed class MVarStore
{
    private const int ShardCount = 16;
    private const int ShardMask = ShardCount - 1;

    private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;

    private readonly ConcurrentDictionary<string, object?>[] _shards;

    public MVarStore()
    {
        _shards = new ConcurrentDictionary<string, object?>[ShardCount];
        for (var i = 0; i < ShardCount; i++)
        {
            _shards[i] = new ConcurrentDictionary<string, object?>(KeyComparer);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ShardIndex(string key) =>
        (KeyComparer.GetHashCode(key) & int.MaxValue) & ShardMask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ConcurrentDictionary<string, object?> Map(string key) => _shards[ShardIndex(key)];

    /// <summary>Writes or overwrites a variable value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Set<T>(string key, T value)
    {
        Map(key)[key] = value;
    }

    /// <summary>Gets a value and tries type conversion when needed.</summary>
    public T? Get<T>(string key)
    {
        if (!Map(key).TryGetValue(key, out var raw) || raw is null)
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
        if (!Map(key).TryGetValue(key, out var raw) || raw is null)
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
        var capacity = 0;
        foreach (var shard in _shards)
        {
            capacity += shard.Count;
        }

        var dict = new Dictionary<string, object?>(Math.Max(capacity, 0), KeyComparer);
        foreach (var shard in _shards)
        {
            foreach (var kv in shard)
            {
                dict[kv.Key] = kv.Value;
            }
        }

        return dict;
    }
}
