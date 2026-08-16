using System.Collections.Concurrent;
using System.Diagnostics;

namespace MDKOSS.Core.Drivers;

/// <summary>
/// Coalesces <see cref="AxisStatus"/> reads within a short TTL so monitoring,
/// alarms, and tasks hitting the same axis share one native scan.
/// Motion commands must <see cref="Invalidate"/>.
/// </summary>
public sealed class DriverAxisStateCache
{
    public const int DefaultTtlMs = 2;

    private readonly long _ttlTicks;
    private readonly ConcurrentDictionary<short, Entry> _map = new();

    public DriverAxisStateCache(int ttlMs = DefaultTtlMs)
    {
        _ttlTicks = Math.Max(1, ttlMs) * Stopwatch.Frequency / 1000L;
    }

    public bool TryGet(short axis, out AxisStatus status)
    {
        if (_map.TryGetValue(axis, out var entry)
            && Stopwatch.GetTimestamp() - entry.Timestamp < _ttlTicks)
        {
            status = entry.Status;
            return true;
        }

        status = default;
        return false;
    }

    public void Set(short axis, in AxisStatus status) =>
        _map[axis] = new Entry(status, Stopwatch.GetTimestamp());

    public void Invalidate(short axis) => _map.TryRemove(axis, out _);

    public void InvalidateAll() => _map.Clear();

    private readonly record struct Entry(AxisStatus Status, long Timestamp);
}
