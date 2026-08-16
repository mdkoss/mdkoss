using System.Collections.Concurrent;
using System.Diagnostics;

namespace MDKOSS.Core.Drivers;

/// <summary>
/// Coalesces digital port-word reads within a short TTL so many bit reads
/// share one native <c>GetDi</c>/<c>GetDo</c> (or DMC inport/outport).
/// Writers must <see cref="Invalidate"/> the affected port.
/// </summary>
public sealed class DriverIoPortCache
{
    public const int DefaultTtlMs = 2;

    private readonly long _ttlTicks;
    private readonly ConcurrentDictionary<int, Entry> _map = new();

    public DriverIoPortCache(int ttlMs = DefaultTtlMs)
    {
        _ttlTicks = Math.Max(1, ttlMs) * Stopwatch.Frequency / 1000L;
    }

    public bool TryGet(bool isOutput, short type, out int word)
    {
        if (_map.TryGetValue(Key(isOutput, type), out var entry)
            && Stopwatch.GetTimestamp() - entry.Timestamp < _ttlTicks)
        {
            word = entry.Word;
            return true;
        }

        word = 0;
        return false;
    }

    public void Set(bool isOutput, short type, int word) =>
        _map[Key(isOutput, type)] = new Entry(word, Stopwatch.GetTimestamp());

    public void Invalidate(bool isOutput, short type) =>
        _map.TryRemove(Key(isOutput, type), out _);

    public void Clear() => _map.Clear();

    private static int Key(bool isOutput, short type) =>
        (isOutput ? 1 << 16 : 0) | (ushort)type;

    private readonly record struct Entry(int Word, long Timestamp);
}
