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
    public Task<T> ExecuteAsync<T>(int roomId, Func<AppDbContext, Room, Task<T>> operation, CancellationToken ct)
    {
        return ExecuteInTransactionAsync(async db =>
        {
            var room = await LockAsync(db, roomId, ct);
            return await operation(db, room);
        }, ct);
    }

    public Task<T> ExecuteInTransactionAsync<T>(Func<AppDbContext, Task<T>> operation, CancellationToken ct)
    {
        var strategy = scopedDb.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync<object?, T>(
            null,
            async (_, _, token) =>
            {
                await using var db = new AppDbContext(options, clock);
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                var result = await operation(db);
                await transaction.CommitAsync(token);
                return result;
            },
            null,
            ct);
    }

    internal static async Task<Room> LockAsync(AppDbContext db, int roomId, CancellationToken ct)
    {
        return await db.Rooms
                   .FromSqlInterpolated($"SELECT * FROM [dbo].[Rooms] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {roomId}")
                   .SingleOrDefaultAsync(ct)
               ?? throw new ApiException(404, "room_not_found", "Room not found.");
    }

    internal static async Task<Poll> LockPollAsync(
        AppDbContext db, int roomId, int pollId, bool includeOptions, CancellationToken ct)
    {
        var query = db.Polls.FromSqlInterpolated(
            $"SELECT * FROM [dbo].[Polls] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {pollId} AND [RoomId] = {roomId}");

        if (includeOptions)
        {
            query = query.Include(p => p.Options);
        }

        return await query.SingleOrDefaultAsync(ct)
               ?? throw new ApiException(404, "poll_not_found", "Poll not found.");
    }

    internal static Task<int[]> LockRoomPollsAsync(AppDbContext db, int roomId, CancellationToken ct)
    {
        return db.Polls
            .FromSqlInterpolated($"SELECT * FROM [dbo].[Polls] WITH (UPDLOCK, HOLDLOCK) WHERE [RoomId] = {roomId}")
            .Select(p => p.Id)
            .ToArrayAsync(ct);
    }
}