using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using MultiPlanerAPI.Modules.Users;

namespace MultiPlanerAPI.UnitTests;

public sealed class SessionValidationCacheTests
{
    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache(options => options.SizeLimit = 100_000);
        services.AddHybridCache();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task CachesAndInvalidatesUserSecurityStamp()
    {
        await using var provider = CreateProvider();
        var cache = new SessionValidationCache(provider.GetRequiredService<HybridCache>());
        var loads = 0;

        var first = await cache.GetOrLoadSecurityStampAsync(1, _ =>
        {
            loads++;
            return ValueTask.FromResult<string?>("stamp-1");
        }, CancellationToken.None);

        var second = await cache.GetOrLoadSecurityStampAsync(1, _ =>
        {
            loads++;
            return ValueTask.FromResult<string?>("stamp-2");
        }, CancellationToken.None);

        Assert.Equal("stamp-1", first);
        Assert.Equal("stamp-1", second);
        Assert.Equal(1, loads);

        await cache.InvalidateUserAsync(1, "stamp-2", CancellationToken.None);

        var third = await cache.GetOrLoadSecurityStampAsync(1, _ =>
        {
            loads++;
            return ValueTask.FromResult<string?>("stamp-3");
        }, CancellationToken.None);

        Assert.Equal("stamp-2", third);
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task CachesAndInvalidatesGuestExpiry()
    {
        await using var provider = CreateProvider();
        var cache = new SessionValidationCache(provider.GetRequiredService<HybridCache>());
        var guestId = Guid.NewGuid();
        var expiry = DateTimeOffset.UtcNow.AddDays(30);
        var loads = 0;

        var first = await cache.GetOrLoadGuestExpiryAsync(guestId, _ =>
        {
            loads++;
            return ValueTask.FromResult<DateTimeOffset?>(expiry);
        }, CancellationToken.None);

        var second = await cache.GetOrLoadGuestExpiryAsync(guestId, _ =>
        {
            loads++;
            return ValueTask.FromResult<DateTimeOffset?>(expiry.AddDays(1));
        }, CancellationToken.None);

        Assert.Equal(expiry, first);
        Assert.Equal(expiry, second);
        Assert.Equal(1, loads);

        await cache.InvalidateGuestAsync(guestId, CancellationToken.None);

        var afterInvalidate = await cache.GetOrLoadGuestExpiryAsync(guestId, _ =>
        {
            loads++;
            return ValueTask.FromResult<DateTimeOffset?>(null);
        }, CancellationToken.None);

        Assert.Null(afterInvalidate);
        Assert.Equal(1, loads);
    }

    [Fact]
    public async Task InvalidationDuringInFlightLoadDoesNotCacheStaleStamp()
    {
        await using var provider = CreateProvider();
        var cache = new SessionValidationCache(provider.GetRequiredService<HybridCache>());

        var dbStamp = "stamp-1";
        var callCount = 0;
        var firstLoadEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstLoadFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var loadTask = cache.GetOrLoadSecurityStampAsync(42, async _ =>
        {
            var call = Interlocked.Increment(ref callCount);
            if (call == 1)
            {
                var snapshot = dbStamp;
                firstLoadEntered.SetResult();
                await allowFirstLoadFinish.Task;
                return snapshot;
            }

            return Volatile.Read(ref dbStamp);
        }, CancellationToken.None).AsTask();

        await firstLoadEntered.Task;
        Volatile.Write(ref dbStamp, "stamp-2");
        await cache.InvalidateUserAsync(42, "stamp-2", CancellationToken.None);
        allowFirstLoadFinish.SetResult();

        var loaded = await loadTask;
        Assert.Equal("stamp-2", loaded);

        var cached = await cache.GetOrLoadSecurityStampAsync(
            42,
            _ => ValueTask.FromResult<string?>("unexpected-reload"),
            CancellationToken.None);

        Assert.Equal("stamp-2", cached);
    }
}