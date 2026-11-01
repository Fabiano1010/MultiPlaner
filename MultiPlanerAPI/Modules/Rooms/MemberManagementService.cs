using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Rooms;
namespace MultiPlanerAPI.Modules.Rooms;

public sealed class MemberManagementService(RoomMutationService mutations, CurrentActor actor, TimeProvider clock)
{
    public Task ChangeRoleAsync(int roomId, int memberId, ChangeMemberRoleRequest request, CancellationToken ct) =>
        MutateAsync(roomId, memberId, request.RowVersion, false, request.IsAdmin, ct);
    public Task RemoveAsync(int roomId, int memberId, RemoveMemberRequest request, CancellationToken ct) =>
        MutateAsync(roomId, memberId, request.RowVersion, true, false, ct);

    private Task MutateAsync(int roomId, int memberId, byte[] version, bool remove, bool admin, CancellationToken ct) =>
        mutations.ExecuteAsync(roomId, async (db, room) => {
            var access = new RoomAccessService(db, actor, clock);
            await access.RequireManagerAsync(roomId, ct);
            access.RequireActive(room);
            var member = await db.RoomMembers.SingleOrDefaultAsync(m => m.RoomId == roomId && m.Id == memberId && m.LeftAtUtc == null, ct)
                ?? throw new ApiException(404, "member_not_found", "Active member not found.");
            if (member.UserId == room.OwnerUserId)
                throw new ApiException(403, "owner_protected", "The owner cannot be removed or demoted.");
            if (member.IsAdmin && room.OwnerUserId != actor.UserId)
                throw new ApiException(403, "room_owner_required", "Only the owner may manage administrators.");
            if (!member.RowVersion.AsSpan().SequenceEqual(version))
                throw new ApiException(409, "concurrency_conflict", "Member changed. Reload and retry.");
            if (!remove && admin && member.UserId == null)
                throw new ApiException(400, "guest_cannot_be_admin", "Guests cannot become administrators.");
            member.IsAdmin = !remove && admin;
            if (remove) {
                member.LeftAtUtc = clock.GetUtcNow();
                member.IsFavourite = false;
                if (member.UserId is {} userId)
                    await db.UserFolderRooms.Where(f => f.OwnerUserId == userId && f.RoomId == roomId).ExecuteDeleteAsync(ct);
            }
            db.Entry(member).Property(m => m.UpdatedAtUtc).IsModified = true;
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
}
