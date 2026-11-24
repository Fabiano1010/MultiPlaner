using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Users;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class RoomAccessService(AppDbContext db, CurrentActor actor, TimeProvider clock)
{
    public async Task<(Room Room, RoomMember Member)> RequireMemberAsync(int roomId, CancellationToken ct)
    {
        var userId = actor.UserId;
        var guestId = actor.GuestSessionId;
        var result = await (from member in db.RoomMembers.AsNoTracking()
                join room in db.Rooms.AsNoTracking() on member.RoomId equals room.Id
                where room.Id == roomId && member.LeftAtUtc == null &&
                      ((userId != null && member.UserId == userId) ||
                       (guestId != null && member.GuestSessionId == guestId))
                select new { Room = room, Member = member })
            .SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "room_not_found", "Room not found.");
        return (result.Room, result.Member);
    }

    public async Task<RoomMember> RequireMemberAsync(Room room, CancellationToken ct)
    {
        if (actor.UserId is { } userId)
        {
            return await db.RoomMembers.AsNoTracking()
                       .SingleOrDefaultAsync(
                           m => m.RoomId == room.Id && m.UserId != null && m.UserId == userId && m.LeftAtUtc == null,
                           ct)
                   ?? throw new ApiException(404, "room_not_found", "Room not found.");
        }

        if (actor.GuestSessionId is { } guestId)
        {
            return await db.RoomMembers.AsNoTracking()
                       .SingleOrDefaultAsync(
                           m => m.RoomId == room.Id && m.GuestSessionId != null && m.GuestSessionId == guestId &&
                                m.LeftAtUtc == null, ct)
                   ?? throw new ApiException(404, "room_not_found", "Room not found.");
        }

        throw new ApiException(404, "room_not_found", "Room not found.");
    }

    public async Task<Room> RequireOwnerAsync(int roomId, CancellationToken ct)
    {
        var userId = actor.RequireUserId();
        var room = await db.Rooms.SingleOrDefaultAsync(r => r.Id == roomId, ct)
                   ?? throw new ApiException(404, "room_not_found", "Room not found.");
        if (room.OwnerUserId != userId)
        {
            throw new ApiException(403, "room_owner_required", "Only the room owner may manage it.");
        }

        return room;
    }

    public async Task<Room> RequireManagerAsync(int roomId, CancellationToken ct)
    {
        actor.RequireUserId();
        var (room, member) = await RequireMemberAsync(roomId, ct);
        if (room.OwnerUserId != actor.UserId && !member.IsAdmin)
        {
            throw new ApiException(403, "room_manager_required",
                "Only the owner or an administrator may manage this room.");
        }

        return room;
    }

    public async Task<RoomMember> RequireManagerAsync(Room room, CancellationToken ct)
    {
        actor.RequireUserId();
        var member = await RequireMemberAsync(room, ct);
        if (room.OwnerUserId != actor.UserId && !member.IsAdmin)
        {
            throw new ApiException(403, "room_manager_required",
                "Only the owner or an administrator may manage this room.");
        }

        return member;
    }

    public void RequireActive(Room room)
    {
        if (room.ArchivedAtUtc is not null || room.ExpiresAtUtc <= clock.GetUtcNow())
        {
            throw new ApiException(409, "room_inactive", "The room is no longer active.");
        }
    }

    public void RequireReadable(Room room)
    {
        var now = clock.GetUtcNow();
        if (room.GetArchivedAtUtc(now) is null && room.ExpiresAtUtc <= now)
        {
            throw new ApiException(410, "room_expired", "The room has expired.");
        }
    }

    public static void RequireOwnEntry(RoomMember member, int authorMemberId, int ownerUserId)
    {
        if (member.Id != authorMemberId && member.UserId != ownerUserId)
        {
            throw new ApiException(403, "entry_author_required", "Only the author or room owner may edit this entry.");
        }
    }
}