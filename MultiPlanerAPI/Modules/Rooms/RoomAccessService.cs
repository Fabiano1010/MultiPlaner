using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Users;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class RoomAccessService(AppDbContext db, CurrentActor actor, TimeProvider clock)
{
    public async Task<(Room Room, RoomMember Member)> RequireMemberAsync(
        int roomId, CancellationToken cancellationToken)
    {
        var userId = actor.UserId;
        var guestId = actor.GuestSessionId;
        var result = await (from member in db.RoomMembers.AsNoTracking()
            join room in db.Rooms.AsNoTracking() on member.RoomId equals room.Id
            where room.Id == roomId &&
                  (userId != null && member.UserId == userId ||
                   guestId != null && member.GuestSessionId == guestId)
            select new { Room = room, Member = member })
            .SingleOrDefaultAsync(cancellationToken);
        if (result is null)
            throw new ApiException(404, "room_not_found", "Room not found.");
        return (result.Room, result.Member);
    }

    public async Task<Room> RequireOwnerAsync(int roomId, CancellationToken cancellationToken)
    {
        var userId = actor.RequireUserId();
        var room = await db.Rooms.SingleOrDefaultAsync(r => r.Id == roomId, cancellationToken)
            ?? throw new ApiException(404, "room_not_found", "Room not found.");
        if (room.OwnerUserId != userId)
            throw new ApiException(403, "room_owner_required", "Only the room owner may manage it.");
        return room;
    }

    public void RequireActive(Room room)
    {
        if (room.ArchivedAtUtc is not null || room.ExpiresAtUtc <= clock.GetUtcNow())
            throw new ApiException(409, "room_inactive", "The room is no longer active.");
    }

    public void RequireReadable(Room room)
    {
        if (room.ArchivedAtUtc is null && room.ExpiresAtUtc <= clock.GetUtcNow())
            throw new ApiException(410, "room_expired", "The room has expired.");
    }

    public static void RequireOwnEntry(int actorMemberId, int authorMemberId)
    {
        if (actorMemberId != authorMemberId)
            throw new ApiException(403, "entry_author_required", "Only the author may edit this entry.");
    }
}
