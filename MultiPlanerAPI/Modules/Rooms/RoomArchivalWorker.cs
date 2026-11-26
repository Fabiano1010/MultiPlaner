using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class RoomArchivalWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<RoomArchivalWorker> logger) : BackgroundService
{
    public static Task<int> ArchiveDueAsync(AppDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        // One conditional statement is safe to repeat and to run on multiple instances.
        return db.Rooms.Where(r => r.ArchiveOnExpiry && r.ArchivedAtUtc == null && r.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(update => update
                .SetProperty(r => r.ArchivedAtUtc, r => r.ExpiresAtUtc)
                .SetProperty(r => r.UpdatedAtUtc, now), cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("RoomArchival:Enabled", true))
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        try
        {
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await ArchiveDueAsync(
                        scope.ServiceProvider.GetRequiredService<AppDbContext>(), clock, stoppingToken);
                }
                catch (Exception error) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(error, "Automatic room archival failed; retrying on the next pass.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }
}