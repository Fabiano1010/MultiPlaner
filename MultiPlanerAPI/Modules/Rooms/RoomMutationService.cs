using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class RoomMutationService(
    AppDbContext scopedDb,
    DbContextOptions<AppDbContext> options,
    TimeProvider clock)
{
    public Task<T> ExecuteAsync<T>(int roomId, Func<AppDbContext, Room, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        return ExecuteInTransactionAsync(async db =>
        {
            var room = await LockAsync(db, roomId, cancellationToken);
            return await operation(db, room);
        }, cancellationToken);
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<AppDbContext, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var strategy = scopedDb.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync<object?, T>(
            null,
            async (_, _, ct) =>
            {
                await using var db = new AppDbContext(options, clock);
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var result = await operation(db);
                await transaction.CommitAsync(ct);
                return result;
            },
            null,
            cancellationToken);
    }

    internal static async Task<Room> LockAsync(AppDbContext db, int roomId, CancellationToken cancellationToken)
    {
        return await db.Rooms
                   .FromSqlInterpolated($"SELECT * FROM [dbo].[Rooms] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {roomId}")
                   .SingleOrDefaultAsync(cancellationToken)
               ?? throw new ApiException(404, "room_not_found", "Room not found.");
    }

    internal static async Task<Poll> LockPollAsync(AppDbContext db, int roomId, int pollId,
        bool includeOptions, CancellationToken cancellationToken)
    {
        var query = db.Polls.FromSqlInterpolated(
            $"SELECT * FROM [dbo].[Polls] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {pollId} AND [RoomId] = {roomId}");

        if (includeOptions)
        {
            query = query.Include(p => p.Options);
        }

        return await query.SingleOrDefaultAsync(cancellationToken)
               ?? throw new ApiException(404, "poll_not_found", "Poll not found.");
    }

    internal static Task<int[]> LockRoomPollsAsync(AppDbContext db, int roomId, CancellationToken cancellationToken)
    {
        return db.Polls
            .FromSqlInterpolated($"SELECT * FROM [dbo].[Polls] WITH (UPDLOCK, HOLDLOCK) WHERE [RoomId] = {roomId}")
            .Select(p => p.Id)
            .ToArrayAsync(cancellationToken);
    }
}
