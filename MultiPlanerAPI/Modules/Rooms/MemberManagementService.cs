using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class MemberManagementService(RoomMutationService mutations, CurrentActor actor, TimeProvider clock)
{
    public Task ChangeRoleAsync(int roomId, int memberId, ChangeMemberRoleRequest request, CancellationToken ct)
    {
        return MutateAsync(roomId, memberId, request.RowVersion, false, request.IsAdmin, ct);
    }

    public Task RemoveAsync(int roomId, int memberId, RemoveMemberRequest request, CancellationToken ct)
    {
        return MutateAsync(roomId, memberId, request.RowVersion, true, false, ct);
    }

    private Task MutateAsync(int roomId, int memberId, byte[] version, bool remove, bool admin, CancellationToken ct)
    {
        return mutations.ExecuteAsManagerAsync(roomId, async (db, room, _) =>
        {
            var member = await db.RoomMembers.SingleOrDefaultAsync(
                             m => m.RoomId == roomId && m.Id == memberId && m.LeftAtUtc == null, ct)
                         ?? throw new ApiException(404, "member_not_found", "Active member not found.");
            if (member.UserId == room.OwnerUserId)
            {
                throw new ApiException(403, "owner_protected", "The owner cannot be removed or demoted.");
            }

            if (member.IsAdmin && room.OwnerUserId != actor.UserId)
            {
                throw new ApiException(403, "room_owner_required", "Only the owner may manage administrators.");
            }

            ApiException.RequireVersion(member.RowVersion, version, "Member changed. Reload and retry.");

            if (!remove && admin && member.UserId is null)
            {
                throw new ApiException(400, "guest_cannot_be_admin", "Guests cannot become administrators.");
            }

            if (remove)
            {
                await RoomMembershipService.DeactivateMemberAsync(db, member, clock.GetUtcNow(), ct);
            }
            else
            {
                member.IsAdmin = admin;
            }

            db.Entry(member).Property(m => m.UpdatedAtUtc).IsModified = true;
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
    }
}