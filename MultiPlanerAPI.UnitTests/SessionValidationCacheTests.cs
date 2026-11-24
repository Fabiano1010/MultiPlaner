using Microsoft.Extensions.Caching.Memory;
using MultiPlanerAPI.Modules.Users;

namespace MultiPlanerAPI.UnitTests;

public sealed class SessionValidationCacheTests
{
    [Fact]
    public void CachesAndInvalidatesUserSecurityStamp()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new SessionValidationCache(memory);

        Assert.False(cache.TryGetSecurityStamp(42, out _));

        cache.SetSecurityStamp(42, "stamp-1");
        Assert.True(cache.TryGetSecurityStamp(42, out var stamp));
        Assert.Equal("stamp-1", stamp);

        cache.InvalidateUser(42);
        Assert.False(cache.TryGetSecurityStamp(42, out _));
    }

    [Fact]
    public void CachesAndInvalidatesGuestExpiryIncludingNullMisses()
    {
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var cache = new SessionValidationCache(memory);
        var guestId = Guid.NewGuid();
        var expiry = DateTimeOffset.UtcNow.AddDays(10);

        Assert.False(cache.TryGetGuestExpiry(guestId, out _));

        cache.SetGuestExpiry(guestId, expiry);
        Assert.True(cache.TryGetGuestExpiry(guestId, out var cached));
        Assert.Equal(expiry, cached);

        cache.InvalidateGuest(guestId);
        Assert.False(cache.TryGetGuestExpiry(guestId, out _));

        cache.SetGuestExpiry(guestId, null);
        Assert.True(cache.TryGetGuestExpiry(guestId, out var revoked));
        Assert.Null(revoked);
    }
}
