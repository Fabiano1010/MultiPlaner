using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Hybrid;

namespace MultiPlanerAPI.Modules.Users;

/// <summary>
/// Caches user security stamps and guest session expiry timestamps via <see cref="HybridCache"/>,
/// protecting against stampede and TOCTOU stale cache re-population on logout/password change.
/// </summary>
public sealed class SessionValidationCache(HybridCache cache)
{
    internal static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(5);

    private static readonly HybridCacheEntryOptions CacheOptions = new()
    {
        Expiration = EntryTtl,
        LocalCacheExpiration = EntryTtl
    };

    private readonly ConcurrentDictionary<int, long> userVersions = new();
    private readonly ConcurrentDictionary<Guid, long> guestVersions = new();

    public async ValueTask<string?> GetOrLoadSecurityStampAsync(
        int userId,
        Func<CancellationToken, ValueTask<string?>> loader,
        CancellationToken cancellationToken)
    {
        var key = UserKey(userId);
        var startVersion = userVersions.GetOrAdd(userId, 0);

        var entry = await cache.GetOrCreateAsync(
            key,
            (userId, loader, versions: userVersions),
            static async (state, ct) =>
            {
                var capturedVersion = state.versions.GetOrAdd(state.userId, 0);
                var stamp = await state.loader(ct);
                if (capturedVersion != state.versions.GetOrAdd(state.userId, 0))
                {
                    stamp = await state.loader(ct);
                }

                return new CachedUserStamp(stamp);
            },
            CacheOptions,
            [UserTag(userId)],
            cancellationToken);

        if (startVersion == userVersions.GetOrAdd(userId, 0))
        {
            return entry.Value;
        }

        // An invalidation raced with HybridCache storing the factory result; refresh directly.
        var fresh = await loader(cancellationToken);
        await cache.SetAsync(key, new CachedUserStamp(fresh), CacheOptions, [UserTag(userId)], cancellationToken);
        return fresh;
    }

    public async ValueTask InvalidateUserAsync(int userId, string? newStamp, CancellationToken cancellationToken)
    {
        userVersions.AddOrUpdate(userId, 1, static (_, current) => current + 1);
        await cache.SetAsync(
            UserKey(userId),
            new CachedUserStamp(newStamp),
            CacheOptions,
            [UserTag(userId)],
            cancellationToken);
    }

    public async ValueTask<DateTimeOffset?> GetOrLoadGuestExpiryAsync(
        Guid guestId,
        Func<CancellationToken, ValueTask<DateTimeOffset?>> loader,
        CancellationToken cancellationToken)
    {
        var key = GuestKey(guestId);
        var startVersion = guestVersions.GetOrAdd(guestId, 0);

        var entry = await cache.GetOrCreateAsync(
            key,
            (guestId, loader, versions: guestVersions),
            static async (state, ct) =>
            {
                var capturedVersion = state.versions.GetOrAdd(state.guestId, 0);
                var expiry = await state.loader(ct);
                if (capturedVersion != state.versions.GetOrAdd(state.guestId, 0))
                {
                    expiry = await state.loader(ct);
                }

                return new CachedGuestExpiry(expiry);
            },
            CacheOptions,
            [GuestTag(guestId)],
            cancellationToken);

        if (startVersion == guestVersions.GetOrAdd(guestId, 0))
        {
            return entry.ExpiresAtUtc;
        }

        var fresh = await loader(cancellationToken);
        await cache.SetAsync(key, new CachedGuestExpiry(fresh), CacheOptions, [GuestTag(guestId)], cancellationToken);
        return fresh;
    }

    public async ValueTask InvalidateGuestAsync(Guid guestId, CancellationToken cancellationToken)
    {
        guestVersions.AddOrUpdate(guestId, 1, static (_, current) => current + 1);
        await cache.SetAsync(
            GuestKey(guestId),
            new CachedGuestExpiry(null),
            CacheOptions,
            [GuestTag(guestId)],
            cancellationToken);
    }

    private static string UserKey(int userId)
    {
        return $"session:user-stamp:{userId}";
    }

    private static string UserTag(int userId)
    {
        return $"user:{userId}";
    }

    private static string GuestKey(Guid guestId)
    {
        return $"session:guest-expiry:{guestId:N}";
    }

    private static string GuestTag(Guid guestId)
    {
        return $"guest:{guestId:N}";
    }
}