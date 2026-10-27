using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;

namespace MultiPlanerAPI.Modules.Rooms;

/// <summary>Serializes room writes with leave/join, archive and deletion. Each retry gets a fresh context.</summary>
public sealed class RoomMutationService(DbContextOptions<AppDbContext> options, TimeProvider clock)
{
    public async Task<T> ExecuteAsync<T>(int roomId, Func<AppDbContext, Room, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await using var strategyDb = new AppDbContext(options, clock);
        return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = new AppDbContext(options, clock);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var room = await LockAsync(db, roomId, cancellationToken);
            var result = await operation(db, room);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    internal static async Task<Room> LockAsync(AppDbContext db, int roomId, CancellationToken cancellationToken) =>
        await db.Rooms.FromSqlInterpolated($"SELECT * FROM [dbo].[Rooms] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {roomId}")
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new ApiException(404, "room_not_found", "Room not found.");
}
