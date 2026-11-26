using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Users;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class RoomMembershipService(RoomMutationService mutations, CurrentActor actor, TimeProvider clock)
{
    public Task LeaveAsync(int roomId, CancellationToken ct)
    {
        return mutations.ExecuteAsync(roomId, async (db, room) =>
        {
            var userId = actor.UserId;
            var guestId = actor.GuestSessionId;
            var member = await db.RoomMembers.SingleOrDefaultAsync(
                             m => m.RoomId == roomId &&
                                  ((userId != null && m.UserId == userId) ||
                                   (guestId != null && m.GuestSessionId == guestId)),
                             ct)
                         ?? throw new ApiException(404, "room_not_found", "Room not found.");

            if (room.OwnerUserId == userId)
            {
                throw new ApiException(409, "owner_cannot_leave", "The owner cannot leave their room.");
            }

            if (member.LeftAtUtc is not null)
            {
                return true;
            }

            await DeactivateMemberAsync(db, member, clock.GetUtcNow(), ct);
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    internal static async Task DeactivateMemberAsync(
        AppDbContext db, RoomMember member, DateTimeOffset now, CancellationToken ct)
    {
        member.LeftAtUtc = now;
        member.IsAdmin = false;
        member.IsFavourite = false;
        if (member.UserId is { } userId)
        {
            await db.UserFolderRooms
                .Where(a => a.OwnerUserId == userId && a.RoomId == member.RoomId)
                .ExecuteDeleteAsync(ct);
        }
    }
}