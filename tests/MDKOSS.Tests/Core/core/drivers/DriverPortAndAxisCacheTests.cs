using MDKOSS.Core.Drivers;

namespace MDKOSS.Tests.Core.Drivers;

public sealed class DriverIoPortCacheTests
{
    [Fact]
    public void Set_then_TryGet_hits_within_ttl()
    {
        var cache = new DriverIoPortCache(ttlMs: 200);
        cache.Set(false, 4, 0x5);
        Assert.True(cache.TryGet(false, 4, out var word));
        Assert.Equal(0x5, word);
        Assert.False(cache.TryGet(true, 4, out _));
        Assert.False(cache.TryGet(false, 12, out _));
    }

    [Fact]
    public void Invalidate_drops_port()
    {
        var cache = new DriverIoPortCache(ttlMs: 200);
        cache.Set(true, 12, 7);
        cache.Invalidate(true, 12);
        Assert.False(cache.TryGet(true, 12, out _));
    }

    [Fact]
    public void Expired_entry_misses()
    {
        var cache = new DriverIoPortCache(ttlMs: 1);
        cache.Set(false, 0, 1);
        Thread.Sleep(20);
        Assert.False(cache.TryGet(false, 0, out _));
    }
}

public sealed class DriverAxisStateCacheTests
{
    [Fact]
    public void Set_then_TryGet_hits_until_invalidate()
    {
        var cache = new DriverAxisStateCache(ttlMs: 200);
        var status = AxisStatus.Create(servoOn: true, prfPosition: 12);
        cache.Set(1, status);
        Assert.True(cache.TryGet(1, out var hit));
        Assert.True(hit.ServoOn);
        Assert.Equal(12, hit.PrfPosition);
        cache.Invalidate(1);
        Assert.False(cache.TryGet(1, out _));
    }
}
