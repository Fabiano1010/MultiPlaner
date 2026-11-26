using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class InvitationService(
    AppDbContext db,
    RoomMutationService mutations,
    CurrentActor actor,
    GuestSessionService guests,
    TimeProvider clock,
    InvitationLinkBuilder links,
    RoomAccessService access,
    IDataProtectionProvider dataProtection)
{
    private readonly IDataProtector protector = InvitationTokens.CreateProtector(dataProtection);

    public async Task<RoomInvitationResponse> CreateAsync(int roomId, CreateRoomInvitationRequest request,
        CancellationToken ct)
    {
        actor.RequireUserId();
        var room = await access.RequireManagerAsync(roomId, ct);
        var now = clock.GetUtcNow();
        access.RequireActive(room);

        var expiry = request.ExpiresAtUtc ?? (now.AddDays(7) < room.ExpiresAtUtc ? now.AddDays(7) : room.ExpiresAtUtc);
        ValidateCreate(request, expiry, now, room.ExpiresAtUtc);
        return await mutations.ExecuteAsManagerAsync(roomId,
            (operationDb, lockedRoom, _) => CreateInvitationAsync(operationDb, lockedRoom, request.MaxUses, expiry, ct),
            ct);
    }

    public async Task<InvitationPreviewResponse> PreviewAsync(string token, CancellationToken ct)
    {
        var (invitation, room) = await FindUsableAsync(token, ct);
        return new InvitationPreviewResponse(room.Id, room.Name, invitation.ExpiresAtUtc);
    }

    public async Task<RoomResponse> JoinAsync(string token, JoinInvitationRequest request, CancellationToken ct)
    {
        var userId = actor.UserId;
        var guestId = actor.GuestSessionId;
        var (invitation, room) = await FindUsableAsync(token, ct);
        var displayName = await GetDisplayNameAsync(userId, guestId, request, ct);
        var newGuestId = userId is null && guestId is null ? Guid.NewGuid() : (Guid?)null;

        var response = await mutations.ExecuteAsync(room.Id,
            (operationDb, lockedRoom) =>
                JoinRoomAsync(operationDb, invitation, lockedRoom, userId, guestId, newGuestId, displayName, ct),
            ct);
        if (newGuestId is { } sessionId)
        {
            await guests.SignInAsync(sessionId, ct);
        }

        return response;
    }

    public Task RevokeAsync(int roomId, Guid invitationId, CancellationToken ct)
    {
        return mutations.ExecuteAsManagerAsync(roomId, async (context, _, _) =>
        {
            var invitation =
                await context.RoomInvitations.SingleOrDefaultAsync(i => i.Id == invitationId && i.RoomId == roomId, ct)
                ?? throw new ApiException(404, "invitation_not_found", "Invitation not found.");
            invitation.RevokedAtUtc ??= clock.GetUtcNow();
            await context.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    public async Task<RoomInvitationResponse> GetCurrentAsync(int roomId, CancellationToken ct)
    {
        var room = await access.RequireManagerAsync(roomId, ct);
        access.RequireActive(room);
        var now = clock.GetUtcNow();
        var invitation = await db.RoomInvitations.AsNoTracking()
                             .Where(i => i.RoomId == roomId && i.RevokedAtUtc == null && i.ExpiresAtUtc > now &&
                                         (i.MaxUses == null || i.UseCount < i.MaxUses))
                             .OrderByDescending(i => i.CreatedAtUtc).FirstOrDefaultAsync(ct)
                         ?? throw new ApiException(404, "invitation_not_found", "No active invitation exists.");
        if (invitation.ProtectedToken is null)
        {
            throw new ApiException(409, "invitation_regeneration_required",
                "This invitation predates recoverable links. Generate a new invitation.");
        }

        var token = protector.Unprotect(invitation.ProtectedToken);
        if (InvitationTokens.Hash(token) != invitation.TokenHash)
        {
            throw new ApiException(500, "invalid_invitation_storage", "Stored invitation could not be verified.");
        }

        return links.CreateResponse(invitation, token);
    }

    private static void ValidateCreate(
        CreateRoomInvitationRequest request, DateTimeOffset expiry, DateTimeOffset now, DateTimeOffset roomExpiry)
    {
        var errors = new Dictionary<string, string[]>();
        if (expiry.Offset != TimeSpan.Zero || expiry <= now || expiry > roomExpiry)
        {
            errors["expiresAtUtc"] = ["Invitation expiry must be UTC, in the future and no later than room expiry."];
        }

        if (request.MaxUses is <= 0)
        {
            errors["maxUses"] = ["Max uses must be greater than zero."];
        }

        if (errors.Count != 0)
        {
            throw ApiException.Validation(errors);
        }
    }

    private async Task<RoomInvitationResponse> CreateInvitationAsync(
        AppDbContext operationDb, Room room, int? maxUses, DateTimeOffset expiry, CancellationToken ct)
    {
        var issuedAt = clock.GetUtcNow();

        if (expiry > room.ExpiresAtUtc)
        {
            throw new ApiException(409, "room_changed", "Room expiry changed. Reload and retry.");
        }

        if (expiry <= issuedAt)
        {
            throw ApiException.Validation("expiresAtUtc", "Invitation expiry must be in the future.");
        }

        await operationDb.RoomInvitations.Where(i => i.RoomId == room.Id && i.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                u => u.SetProperty(i => i.RevokedAtUtc, issuedAt).SetProperty(i => i.UpdatedAtUtc, issuedAt), ct);

        var token = InvitationTokens.Generate();
        var invitation = new RoomInvitation
        {
            RoomId = room.Id, TokenHash = InvitationTokens.Hash(token), ProtectedToken = protector.Protect(token),
            ExpiresAtUtc = expiry, MaxUses = maxUses
        };
        operationDb.RoomInvitations.Add(invitation);
        await operationDb.SaveChangesAsync(ct);

        return links.CreateResponse(invitation, token);
    }

    private async Task<string> GetDisplayNameAsync(int? userId, Guid? guestId, JoinInvitationRequest request,
        CancellationToken ct)
    {
        if (userId is { } id)
        {
            return await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.DisplayName).SingleAsync(ct);
        }

        if (guestId is { } sessionId)
        {
            return await db.GuestSessions.AsNoTracking().Where(s => s.Id == sessionId).Select(s => s.DisplayName)
                .SingleAsync(ct);
        }

        if (request.DisplayName is null)
        {
            throw ApiException.Validation("displayName", "A guest display name is required.");
        }

        var errors = LocaleValidation.GetErrors(request.DisplayName, null, null);
        if (errors.Count != 0)
        {
            throw ApiException.Validation(errors);
        }

        return request.DisplayName.Trim();
    }

    private async Task<RoomResponse> JoinRoomAsync(
        AppDbContext operationDb, RoomInvitation invitation, Room room, int? userId, Guid? guestId, Guid? newGuestId,
        string displayName, CancellationToken ct)
    {
        access.RequireActive(room);

        var existing = await operationDb.RoomMembers.FirstOrDefaultAsync(m =>
            m.RoomId == room.Id &&
            ((userId != null && m.UserId == userId) ||
             (guestId != null && m.GuestSessionId == guestId) ||
             (newGuestId != null && m.GuestSessionId == newGuestId)), ct);
        if (existing is { LeftAtUtc: null })
        {
            var currentOwner = await operationDb.Users.Where(u => u.Id == room.OwnerUserId).Select(u => u.DisplayName)
                .SingleAsync(ct);
            return RoomService.ToResponse(room, existing, clock.GetUtcNow(), currentOwner);
        }

        var now = clock.GetUtcNow();
        await ConsumeInvitationAsync(operationDb, invitation.Id, now, ct);

        var member = existing ?? AddParticipant(operationDb, room.Id, userId, guestId, newGuestId, displayName, now);
        member.LeftAtUtc = null;
        member.IsAdmin = false;
        await operationDb.SaveChangesAsync(ct);

        var ownerName = await operationDb.Users.Where(u => u.Id == room.OwnerUserId).Select(u => u.DisplayName)
            .SingleAsync(ct);
        return RoomService.ToResponse(room, member, clock.GetUtcNow(), ownerName);
    }

    private static RoomMember AddParticipant(
        AppDbContext operationDb, int roomId, int? userId, Guid? guestId, Guid? newGuestId, string displayName,
        DateTimeOffset now)
    {
        if (newGuestId is { } sessionId)
        {
            operationDb.GuestSessions.Add(new GuestSession
                { Id = sessionId, DisplayName = displayName, ExpiresAtUtc = now.AddDays(30) });
        }

        var member = new RoomMember
        {
            RoomId = roomId, UserId = userId, GuestSessionId = guestId ?? newGuestId, DisplayName = displayName
        };
        operationDb.RoomMembers.Add(member);
        return member;
    }

    private static async Task ConsumeInvitationAsync(AppDbContext operationDb, Guid invitationId, DateTimeOffset now,
        CancellationToken ct)
    {
        var consumed = await operationDb.RoomInvitations
            .Where(i => i.Id == invitationId && i.RevokedAtUtc == null && i.ExpiresAtUtc > now &&
                        (i.MaxUses == null || i.UseCount < i.MaxUses) &&
                        operationDb.Rooms.Any(r => r.Id == i.RoomId && r.ArchivedAtUtc == null && r.ExpiresAtUtc > now))
            .ExecuteUpdateAsync(
                u => u.SetProperty(i => i.UseCount, i => i.UseCount + 1).SetProperty(i => i.UpdatedAtUtc, now), ct);
        if (consumed == 0)
        {
            throw new ApiException(410, "invitation_unavailable", "Invitation has expired or reached its use limit.");
        }
    }

    private async Task<(RoomInvitation Invitation, Room Room)> FindUsableAsync(string token, CancellationToken ct)
    {
        if (!InvitationTokens.IsWellFormed(token))
        {
            throw new ApiException(404, "invitation_not_found", "Invitation not found.");
        }

        var hash = InvitationTokens.Hash(token);
        var invitation = await db.RoomInvitations.AsNoTracking().SingleOrDefaultAsync(i => i.TokenHash == hash, ct)
                         ?? throw new ApiException(404, "invitation_not_found", "Invitation not found.");
        var room = await db.Rooms.AsNoTracking().SingleAsync(r => r.Id == invitation.RoomId, ct);
        var now = clock.GetUtcNow();
        if (room.ArchivedAtUtc is not null || room.ExpiresAtUtc <= now || invitation.RevokedAtUtc is not null ||
            invitation.ExpiresAtUtc <= now || (invitation.MaxUses is { } max && invitation.UseCount >= max))
        {
            throw new ApiException(410, "invitation_unavailable", "Invitation has expired or reached its use limit.");
        }

        return (invitation, room);
    }
}