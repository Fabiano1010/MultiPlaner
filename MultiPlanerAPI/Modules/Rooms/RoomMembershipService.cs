using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Infrastructure;
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

            member.LeftAtUtc = clock.GetUtcNow();
            member.IsAdmin = false;
            member.IsFavourite = false;

            if (userId is not null)
            {
                await db.UserFolderRooms
                    .Where(a => a.OwnerUserId == userId && a.RoomId == roomId)
                    .ExecuteDeleteAsync(ct);
            }

            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
    }
}