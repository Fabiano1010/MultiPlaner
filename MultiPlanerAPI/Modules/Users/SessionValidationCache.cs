using Microsoft.Extensions.Caching.Memory;

namespace MultiPlanerAPI.Modules.Users;

/// <summary>
/// Caches user security stamps and guest session expiry timestamps in memory so cookie validation
/// avoids per-request SQL queries while preserving instant invalidation on logout/password change.
/// </summary>
public sealed class SessionValidationCache(IMemoryCache cache)
{
    private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(5);

    private sealed record StampEntry(string? Stamp);
    private sealed record GuestEntry(DateTimeOffset? ExpiresAtUtc);

    public bool TryGetSecurityStamp(int userId, out string? stamp)
    {
        if (cache.TryGetValue(UserKey(userId), out StampEntry? entry) && entry is not null)
        {
            stamp = entry.Stamp;
            return true;
        }

        stamp = null;
        return false;
    }

    public void SetSecurityStamp(int userId, string? stamp)
    {
        cache.Set(UserKey(userId), new StampEntry(stamp), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = EntryTtl
        });
    }

    public void InvalidateUser(int userId)
    {
        cache.Remove(UserKey(userId));
    }

    public bool TryGetGuestExpiry(Guid guestId, out DateTimeOffset? expiresAtUtc)
    {
        if (cache.TryGetValue(GuestKey(guestId), out GuestEntry? entry) && entry is not null)
        {
            expiresAtUtc = entry.ExpiresAtUtc;
            return true;
        }

        expiresAtUtc = null;
        return false;
    }

    public void SetGuestExpiry(Guid guestId, DateTimeOffset? expiresAtUtc)
    {
        cache.Set(GuestKey(guestId), new GuestEntry(expiresAtUtc), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = EntryTtl
        });
    }

    public void InvalidateGuest(Guid guestId)
    {
        cache.Remove(GuestKey(guestId));
    }

    private static string UserKey(int userId) => $"session:user-stamp:{userId}";
    private static string GuestKey(Guid guestId) => $"session:guest-expiry:{guestId:N}";
}
