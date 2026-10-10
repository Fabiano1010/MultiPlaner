using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class InvitationService(
    AppDbContext db, DbContextOptions<AppDbContext> dbOptions, CurrentActor actor,
    GuestSessionService guests, TimeProvider clock, InvitationLinkBuilder links,
    RoomAccessService access)
{
    public async Task<RoomInvitationResponse> CreateAsync(
        int roomId, CreateRoomInvitationRequest request, CancellationToken cancellationToken)
    {
        var userId = actor.RequireUserId();
        var room = await access.RequireOwnerAsync(roomId, cancellationToken);
        var now = clock.GetUtcNow();
        access.RequireActive(room);

        var expiry = request.ExpiresAtUtc ?? Min(now.AddDays(7), room.ExpiresAtUtc);
        ValidateCreate(request, expiry, now, room.ExpiresAtUtc);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(() => CreateInvitationAsync(roomId, userId, request.MaxUses, expiry,
            cancellationToken));
    }

    private static void ValidateCreate(CreateRoomInvitationRequest request, DateTimeOffset expiry,
        DateTimeOffset now, DateTimeOffset roomExpiry)
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

    private async Task<RoomInvitationResponse> CreateInvitationAsync(int roomId, int userId, int? maxUses,
        DateTimeOffset expiry, CancellationToken cancellationToken)
    {
        await using var operationDb = new AppDbContext(dbOptions, clock);
        await using var transaction = await operationDb.Database.BeginTransactionAsync(cancellationToken);
        var issuedAt = clock.GetUtcNow();
        await LockRoomAsync(operationDb, roomId, userId, issuedAt, cancellationToken);
        if (expiry <= issuedAt)
        {
            throw ApiException.Validation(new Dictionary<string, string[]>
                { ["expiresAtUtc"] = ["Invitation expiry must be in the future."] });
        }

        await RevokeActiveInvitationsAsync(operationDb, roomId, issuedAt, cancellationToken);
        var token = InvitationTokens.Generate();
        var invitation = new RoomInvitation
        {
            RoomId = roomId,
            TokenHash = InvitationTokens.Hash(token),
            ExpiresAtUtc = expiry,
            MaxUses = maxUses
        };
        operationDb.RoomInvitations.Add(invitation);
        await operationDb.SaveChangesAsync(cancellationToken);
        var response = links.CreateResponse(invitation, token);
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    private static Task<int> RevokeActiveInvitationsAsync(AppDbContext operationDb, int roomId,
        DateTimeOffset issuedAt, CancellationToken cancellationToken) =>
        operationDb.RoomInvitations.Where(i => i.RoomId == roomId && i.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update
                .SetProperty(i => i.RevokedAtUtc, issuedAt)
                .SetProperty(i => i.UpdatedAtUtc, issuedAt), cancellationToken);

    /// <summary>Serializes concurrent invitation rotations by updating the owning room.</summary>
    private static async Task LockRoomAsync(AppDbContext operationDb, int roomId, int userId,
        DateTimeOffset issuedAt, CancellationToken cancellationToken)
    {
        var locked = await operationDb.Rooms.Where(r => r.Id == roomId && r.OwnerUserId == userId &&
                r.ArchivedAtUtc == null && r.ExpiresAtUtc > issuedAt)
            .ExecuteUpdateAsync(update => update.SetProperty(r => r.UpdatedAtUtc, issuedAt), cancellationToken);
        if (locked == 0)
        {
            throw new ApiException(409, "room_inactive", "The room is no longer active.");
        }
    }

    public async Task<InvitationPreviewResponse> PreviewAsync(string token, CancellationToken cancellationToken)
    {
        var (invitation, room) = await FindUsableAsync(token, cancellationToken);
        return new InvitationPreviewResponse(room.Id, room.Name, invitation.ExpiresAtUtc);
    }

    public async Task<RoomResponse> JoinAsync(
        string token, JoinInvitationRequest request, CancellationToken cancellationToken)
    {
        var userId = actor.UserId;
        var guestId = actor.GuestSessionId;
        var (invitation, room) = await FindUsableAsync(token, cancellationToken);
        var existing = await db.RoomMembers.AsNoTracking().FirstOrDefaultAsync(m =>
            m.RoomId == room.Id &&
            (userId != null && m.UserId == userId || guestId != null && m.GuestSessionId == guestId),
            cancellationToken);
        if (existing is not null)
        {
            return RoomService.ToResponse(room, existing, clock.GetUtcNow());
        }

        var displayName = await GetDisplayNameAsync(userId, guestId, request, cancellationToken);
        var newGuestId = userId is null && guestId is null ? Guid.NewGuid() : (Guid?)null;
        var strategy = db.Database.CreateExecutionStrategy();
        var joinedRoom = await strategy.ExecuteAsync(() => JoinRoomAsync(invitation, room, userId, guestId,
            newGuestId, displayName, cancellationToken));
        if (newGuestId is { } sessionToSignIn)
        {
            await guests.SignInAsync(sessionToSignIn, cancellationToken);
        }

        return joinedRoom;
    }

    private async Task<string> GetDisplayNameAsync(int? userId, Guid? guestId,
        JoinInvitationRequest request, CancellationToken cancellationToken)
    {
        string displayName;
        if (userId is { } id)
        {
            displayName = await db.Users.AsNoTracking().Where(u => u.Id == id)
                .Select(u => u.DisplayName).SingleAsync(cancellationToken);
        }
        else if (guestId is { } sessionId)
        {
            displayName = await db.GuestSessions.AsNoTracking().Where(s => s.Id == sessionId)
                .Select(s => s.DisplayName).SingleAsync(cancellationToken);
        }
        else
        {
            if (request.DisplayName is null)
            {
                throw ApiException.Validation(new Dictionary<string, string[]>
                    { ["displayName"] = ["A guest display name is required."] });
            }

            var nameErrors = LocaleValidation.GetErrors(request.DisplayName, null, null);
            if (nameErrors.Count != 0)
            {
                throw ApiException.Validation(nameErrors);
            }

            displayName = request.DisplayName.Trim();
        }
        return displayName;
    }

    private async Task<RoomResponse> JoinRoomAsync(RoomInvitation invitation, Room room, int? userId,
        Guid? guestId, Guid? newGuestId, string displayName, CancellationToken cancellationToken)
    {
        await using var operationDb = new AppDbContext(dbOptions, clock);
        await using var transaction = await operationDb.Database.BeginTransactionAsync(cancellationToken);
        var existingMember = await operationDb.RoomMembers.AsNoTracking().FirstOrDefaultAsync(m =>
            m.RoomId == room.Id &&
            (userId != null && m.UserId == userId ||
             guestId != null && m.GuestSessionId == guestId ||
             newGuestId != null && m.GuestSessionId == newGuestId), cancellationToken);
        if (existingMember is not null)
        {
            return RoomService.ToResponse(room, existingMember, clock.GetUtcNow());
        }

        var now = clock.GetUtcNow();
        await ConsumeInvitationAsync(operationDb, invitation.Id, now, cancellationToken);
        var member = AddParticipant(operationDb, room.Id, userId, guestId, newGuestId, displayName, now);
        await operationDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return RoomService.ToResponse(room, member, clock.GetUtcNow());
    }

    private static RoomMember AddParticipant(AppDbContext operationDb, int roomId, int? userId,
        Guid? guestId, Guid? newGuestId, string displayName, DateTimeOffset now)
    {
        if (newGuestId is { } sessionId)
        {
            operationDb.GuestSessions.Add(new GuestSession
            {
                Id = sessionId, DisplayName = displayName, ExpiresAtUtc = now.AddDays(30)
            });
        }
        var member = new RoomMember
        {
            RoomId = roomId,
            UserId = userId,
            GuestSessionId = guestId ?? newGuestId,
            DisplayName = displayName
        };
        operationDb.RoomMembers.Add(member);
        return member;
    }

    private static async Task ConsumeInvitationAsync(AppDbContext operationDb, Guid invitationId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var consumed = await operationDb.RoomInvitations.Where(i => i.Id == invitationId &&
                i.RevokedAtUtc == null && i.ExpiresAtUtc > now &&
                (i.MaxUses == null || i.UseCount < i.MaxUses) &&
                operationDb.Rooms.Any(r => r.Id == i.RoomId && r.ArchivedAtUtc == null && r.ExpiresAtUtc > now))
            .ExecuteUpdateAsync(update => update
                .SetProperty(i => i.UseCount, i => i.UseCount + 1)
                .SetProperty(i => i.UpdatedAtUtc, now), cancellationToken);
        if (consumed == 0)
        {
            throw new ApiException(410, "invitation_unavailable", "Invitation has expired or reached its use limit.");
        }
    }

    public async Task RevokeAsync(int roomId, Guid invitationId, CancellationToken cancellationToken)
    {
        await access.RequireOwnerAsync(roomId, cancellationToken);
        var invitation = await db.RoomInvitations.SingleOrDefaultAsync(
            i => i.Id == invitationId && i.RoomId == roomId, cancellationToken)
            ?? throw new ApiException(404, "invitation_not_found", "Invitation not found.");
        if (invitation.RevokedAtUtc is not null)
        {
            return;
        }

        invitation.RevokedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(RoomInvitation Invitation, Room Room)> FindUsableAsync(
        string token, CancellationToken cancellationToken)
    {
        if (!InvitationTokens.IsWellFormed(token))
        {
            throw new ApiException(404, "invitation_not_found", "Invitation not found.");
        }

        var hash = InvitationTokens.Hash(token);
        var invitation = await db.RoomInvitations.AsNoTracking().SingleOrDefaultAsync(
            i => i.TokenHash == hash, cancellationToken)
            ?? throw new ApiException(404, "invitation_not_found", "Invitation not found.");
        var room = await db.Rooms.AsNoTracking().SingleAsync(r => r.Id == invitation.RoomId, cancellationToken);
        var now = clock.GetUtcNow();
        if (room.ArchivedAtUtc is not null || room.ExpiresAtUtc <= now || invitation.RevokedAtUtc is not null ||
            invitation.ExpiresAtUtc <= now || invitation.MaxUses is { } max && invitation.UseCount >= max)
        {
            throw new ApiException(410, "invitation_unavailable", "Invitation has expired or reached its use limit.");
        }

        return (invitation, room);
    }

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) =>
        first < second ? first : second;

}
