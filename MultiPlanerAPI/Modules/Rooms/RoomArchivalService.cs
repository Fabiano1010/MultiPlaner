using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class RoomArchivalService(AppDbContext db, TimeProvider clock)
{
    public Task<int> ArchiveDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        // One conditional statement is safe to repeat and to run on multiple instances.
        return db.Rooms.Where(r => r.ArchiveOnExpiry && r.ArchivedAtUtc == null && r.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(update => update
                .SetProperty(r => r.ArchivedAtUtc, r => r.ExpiresAtUtc)
                .SetProperty(r => r.UpdatedAtUtc, now), cancellationToken);
    }
}
