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

    public ValueTask<string?> GetOrLoadSecurityStampAsync(
        int userId,
        Func<CancellationToken, ValueTask<string?>> loader,
        CancellationToken cancellationToken)
    {
        return GetOrLoadAsync(
            userVersions,
            userId,
            UserKey(userId),
            UserTag(userId),
            loader,
            static value => new CachedUserStamp(value),
            static entry => entry.Value,
            cancellationToken);
    }

    public ValueTask InvalidateUserAsync(int userId, string? newStamp, CancellationToken cancellationToken)
    {
        return InvalidateAsync(userVersions, userId, UserKey(userId), UserTag(userId), new CachedUserStamp(newStamp),
            cancellationToken);
    }

    public ValueTask<DateTimeOffset?> GetOrLoadGuestExpiryAsync(
        Guid guestId,
        Func<CancellationToken, ValueTask<DateTimeOffset?>> loader,
        CancellationToken cancellationToken)
    {
        return GetOrLoadAsync(
            guestVersions,
            guestId,
            GuestKey(guestId),
            GuestTag(guestId),
            loader,
            static value => new CachedGuestExpiry(value),
            static entry => entry.ExpiresAtUtc,
            cancellationToken);
    }

    public ValueTask InvalidateGuestAsync(Guid guestId, CancellationToken cancellationToken)
    {
        return InvalidateAsync(guestVersions, guestId, GuestKey(guestId), GuestTag(guestId),
            new CachedGuestExpiry(null), cancellationToken);
    }

    private async ValueTask<TValue> GetOrLoadAsync<TId, TValue, TEntry>(
        ConcurrentDictionary<TId, long> versions,
        TId id,
        string key,
        string tag,
        Func<CancellationToken, ValueTask<TValue>> loader,
        Func<TValue, TEntry> wrap,
        Func<TEntry, TValue> unwrap,
        CancellationToken cancellationToken)
        where TId : notnull
    {
        var startVersion = versions.GetOrAdd(id, 0);

        var entry = await cache.GetOrCreateAsync(
            key,
            (id, loader, versions, wrap),
            static async (state, ct) =>
            {
                var capturedVersion = state.versions.GetOrAdd(state.id, 0);
                var value = await state.loader(ct);
                if (capturedVersion != state.versions.GetOrAdd(state.id, 0))
                {
                    value = await state.loader(ct);
                }

                return state.wrap(value);
            },
            CacheOptions,
            [tag],
            cancellationToken);

        if (startVersion == versions.GetOrAdd(id, 0))
        {
            return unwrap(entry);
        }

        // An invalidation raced with HybridCache storing the factory result; refresh directly.
        var fresh = await loader(cancellationToken);
        await cache.SetAsync(key, wrap(fresh), CacheOptions, [tag], cancellationToken);
        return fresh;
    }

    private ValueTask InvalidateAsync<TId, TEntry>(
        ConcurrentDictionary<TId, long> versions,
        TId id,
        string key,
        string tag,
        TEntry entry,
        CancellationToken cancellationToken)
        where TId : notnull
    {
        versions.AddOrUpdate(id, 1, static (_, current) => current + 1);
        return cache.SetAsync(key, entry, CacheOptions, [tag], cancellationToken);
    }

    private static string UserKey(int userId) => $"session:user-stamp:{userId}";
    private static string UserTag(int userId) => $"user:{userId}";
    private static string GuestKey(Guid guestId) => $"session:guest-expiry:{guestId:N}";
    private static string GuestTag(Guid guestId) => $"guest:{guestId:N}";
}