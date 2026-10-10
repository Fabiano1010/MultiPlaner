using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Common;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class RoomService(
    AppDbContext db, DbContextOptions<AppDbContext> dbOptions, CurrentActor actor,
    TimeProvider clock, InvitationLinkBuilder links, RoomAccessService access)
{
    public async Task<RoomResponse> CreateAsync(CreateRoomRequest request, CancellationToken cancellationToken)
    {
        var userId = actor.RequireUserId();
        var now = clock.GetUtcNow();
        var expiry = request.ExpiresAtUtc ?? now.AddMonths(3);
        ValidateCreate(request, now, expiry);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, cancellationToken);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(() => CreateRoomAsync(request, user, now, expiry, cancellationToken));
    }

    private static void ValidateCreate(CreateRoomRequest request, DateTimeOffset now, DateTimeOffset expiry)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length < 2)
        {
            errors["name"] = ["Room name must contain at least two non-whitespace characters."];
        }

        var zone = request.TimeZoneId;
        if (zone is not null && LocaleValidation.GetErrors(null, null, zone).TryGetValue("timeZoneId", out var zoneErrors))
        {
            errors["timeZoneId"] = zoneErrors;
        }

        if (expiry.Offset != TimeSpan.Zero || expiry <= now || expiry > now.AddDays(365))
        {
            errors["expiresAtUtc"] = ["Room expiry must be UTC, in the future and within one year."];
        }

        if (errors.Count != 0)
        {
            throw ApiException.Validation(errors);
        }
    }

    private async Task<RoomResponse> CreateRoomAsync(CreateRoomRequest request, ApplicationUser user,
        DateTimeOffset now, DateTimeOffset expiry, CancellationToken cancellationToken)
    {
        await using var operationDb = new AppDbContext(dbOptions, clock);
        await using var transaction = await operationDb.Database.BeginTransactionAsync(cancellationToken);
        var room = new Room
        {
            OwnerUserId = user.Id,
            Name = request.Name.Trim(),
            TimeZoneId = request.TimeZoneId ?? user.TimeZoneId,
            ExpiresAtUtc = expiry,
            ArchiveOnExpiry = request.ArchiveOnExpiry
        };
        operationDb.Rooms.Add(room);
        await operationDb.SaveChangesAsync(cancellationToken);
        var member = new RoomMember { RoomId = room.Id, UserId = user.Id, DisplayName = user.DisplayName };
        operationDb.RoomMembers.Add(member);
        var token = InvitationTokens.Generate();
        var invitation = new RoomInvitation
        {
            RoomId = room.Id,
            TokenHash = InvitationTokens.Hash(token),
            ExpiresAtUtc = now.AddDays(7) < room.ExpiresAtUtc ? now.AddDays(7) : room.ExpiresAtUtc
        };
        operationDb.RoomInvitations.Add(invitation);
        await operationDb.SaveChangesAsync(cancellationToken);
        var response = ToResponse(room, member, clock.GetUtcNow()) with { InitialInvitation = links.CreateResponse(invitation, token) };
        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<IReadOnlyList<RoomResponse>> ListAsync(
        string status, CancellationToken cancellationToken)
    {
        if (status is not ("active" or "archived" or "all"))
        {
            throw ApiException.Validation(new Dictionary<string, string[]>
                { ["status"] = ["Use active, archived or all."] });
        }

        var userId = actor.UserId;
        var guestId = actor.GuestSessionId;
        var now = clock.GetUtcNow();
        var rooms = await (from member in db.RoomMembers.AsNoTracking()
            join room in db.Rooms.AsNoTracking() on member.RoomId equals room.Id
            where (userId != null && member.UserId == userId ||
                   guestId != null && member.GuestSessionId == guestId) &&
                  (status == "active" && room.ArchivedAtUtc == null && room.ExpiresAtUtc > now ||
                   status == "archived" && (room.ArchivedAtUtc != null || room.ArchiveOnExpiry && room.ExpiresAtUtc <= now) ||
                   status == "all" && (room.ArchivedAtUtc != null || room.ExpiresAtUtc > now || room.ArchiveOnExpiry))
            orderby room.Id descending
            select new { Room = room, Member = member })
            .ToListAsync(cancellationToken);
        return rooms.Select(item => ToResponse(item.Room, item.Member, now)).ToList();
    }

    public async Task<RoomResponse> GetAsync(int roomId, CancellationToken cancellationToken)
    {
        var (room, member) = await access.RequireMemberAsync(roomId, cancellationToken);
        access.RequireReadable(room);
        return ToResponse(room, member, clock.GetUtcNow());
    }

    public async Task<RoomMembershipResponse> GetMembershipAsync(int roomId, CancellationToken cancellationToken)
    {
        var (room, member) = await access.RequireMemberAsync(roomId, cancellationToken);
        access.RequireReadable(room);
        return ToMembershipResponse(member);
    }

    public async Task<RoomMembershipResponse> UpdateMembershipAsync(
        int roomId, UpdateRoomMembershipRequest request, CancellationToken cancellationToken)
    {
        var (room, member) = await access.RequireMemberAsync(roomId, cancellationToken);
        access.RequireReadable(room);
        // A private favourite can change in an archive; public author details cannot.
        if (request.DisplayName is not null || request.Color is not null)
        {
            access.RequireActive(room);
        }

        var errors = LocaleValidation.GetErrors(request.DisplayName, null, null);
        if (errors.Count != 0)
        {
            throw ApiException.Validation(errors);
        }

        if (!member.RowVersion.AsSpan().SequenceEqual(request.RowVersion))
        {
            throw new ApiException(409, "concurrency_conflict", "Your room settings changed. Reload them and retry.");
        }

        db.RoomMembers.Attach(member);
        if (request.DisplayName is not null)
        {
            member.DisplayName = request.DisplayName.Trim();
        }

        if (request.Color is not null)
        {
            member.Color = request.Color.ToUpperInvariant();
        }

        if (request.IsFavourite is { } favourite)
        {
            member.IsFavourite = favourite;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToMembershipResponse(member);
    }

    public async Task<PagedResponse<RoomMemberResponse>> ListMembersAsync(
        int roomId, PageRequest page, CancellationToken cancellationToken)
    {
        var (room, _) = await access.RequireMemberAsync(roomId, cancellationToken);
        access.RequireReadable(room);
        var query = db.RoomMembers.AsNoTracking().Where(m => m.RoomId == roomId);
        var count = await query.CountAsync(cancellationToken);
        var offset = (long)(page.Page - 1) * page.PageSize;
        if (offset > int.MaxValue)
        {
            return new PagedResponse<RoomMemberResponse>([], count, page.Page, page.PageSize);
        }

        var members = await query.OrderBy(m => m.Id)
            .Skip((int)offset).Take(page.PageSize)
            .Select(m => new RoomMemberResponse(m.Id, m.DisplayName, m.Color,
                m.GuestSessionId != null, m.UserId == room.OwnerUserId, m.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return new PagedResponse<RoomMemberResponse>(members, count, page.Page, page.PageSize);
    }

    public async Task<RoomResponse> UpdateAsync(
        int roomId, UpdateRoomRequest request, CancellationToken cancellationToken)
    {
        var room = await access.RequireOwnerAsync(roomId, cancellationToken);
        access.RequireActive(room);
        ValidateUpdate(request, room, clock.GetUtcNow());
        ApplyUpdate(request, room);
        await db.SaveChangesAsync(cancellationToken);
        var (_, member) = await access.RequireMemberAsync(roomId, cancellationToken);
        return ToResponse(room, member, clock.GetUtcNow());
    }

    private static void ValidateUpdate(UpdateRoomRequest request, Room room, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Name is not null && request.Name.Trim().Length < 2)
        {
            errors["name"] = ["Room name must contain at least two non-whitespace characters."];
        }

        if (request.TimeZoneId is not null &&
            LocaleValidation.GetErrors(null, null, request.TimeZoneId)
                .TryGetValue("timeZoneId", out var zoneErrors))
        {
            errors["timeZoneId"] = zoneErrors;
        }

        if (request.ExpiresAtUtc is { } expiry &&
            (expiry.Offset != TimeSpan.Zero || expiry <= now ||
             expiry > room.CreatedAtUtc.AddDays(365)))
        {
            errors["expiresAtUtc"] = ["Room expiry must be UTC, in the future and within one year of creation."];
        }

        if (errors.Count != 0)
        {
            throw ApiException.Validation(errors);
        }
    }

    private static void ApplyUpdate(UpdateRoomRequest request, Room room)
    {
        if (request.Name is not null)
        {
            room.Name = request.Name.Trim();
        }

        if (request.TimeZoneId is not null)
        {
            room.TimeZoneId = request.TimeZoneId;
        }

        if (request.ExpiresAtUtc is { } newExpiry)
        {
            room.ExpiresAtUtc = newExpiry;
        }
    }

    public async Task ArchiveAsync(int roomId, CancellationToken cancellationToken)
    {
        var room = await access.RequireOwnerAsync(roomId, cancellationToken);
        if (room.GetArchivedAtUtc(clock.GetUtcNow()) is not null)
        {
            return;
        }

        access.RequireActive(room);
        room.ArchivedAtUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int roomId, CancellationToken cancellationToken)
    {
        var ownerId = actor.RequireUserId();
        await access.RequireOwnerAsync(roomId, cancellationToken);
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(() => DeleteRoomAsync(roomId, ownerId, cancellationToken));
    }

    private async Task DeleteRoomAsync(int roomId, int ownerId, CancellationToken cancellationToken)
    {
        await using var operationDb = new AppDbContext(dbOptions, clock);
        await using var transaction = await operationDb.Database.BeginTransactionAsync(cancellationToken);
        var locked = await operationDb.Rooms
            .Where(r => r.Id == roomId && r.OwnerUserId == ownerId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(r => r.UpdatedAtUtc, clock.GetUtcNow()), cancellationToken);
        if (locked == 0)
        {
            throw new ApiException(404, "room_not_found", "Room not found.");
        }

        await DeleteRoomContentsAsync(operationDb, roomId, cancellationToken);
        await operationDb.Rooms.Where(r => r.Id == roomId)
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task DeleteRoomContentsAsync(AppDbContext operationDb, int roomId,
        CancellationToken cancellationToken)
    {
        await operationDb.EventParticipants.Where(p => p.RoomId == roomId)
            .ExecuteDeleteAsync(cancellationToken);
        var eventIds = operationDb.CalendarEvents.Where(e => e.RoomId == roomId).Select(e => e.Id);
        await operationDb.EventAttachments.Where(a => eventIds.Contains(a.EventId))
            .ExecuteDeleteAsync(cancellationToken);
        await operationDb.AvailabilityIntervals.Where(a => a.RoomId == roomId)
            .ExecuteDeleteAsync(cancellationToken);
        await operationDb.ChatMessages.Where(m => m.RoomId == roomId)
            .ExecuteDeleteAsync(cancellationToken);
        await operationDb.RoomActivities.Where(a => a.RoomId == roomId)
            .ExecuteDeleteAsync(cancellationToken);
        await operationDb.CalendarEvents.Where(e => e.RoomId == roomId)
            .ExecuteDeleteAsync(cancellationToken);
        await operationDb.RoomInvitations.Where(i => i.RoomId == roomId)
            .ExecuteDeleteAsync(cancellationToken);
        await operationDb.OutboxMessages.Where(m => m.RoomId == roomId)
            .ExecuteDeleteAsync(cancellationToken);
        await operationDb.RoomMembers.Where(m => m.RoomId == roomId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    internal static RoomResponse ToResponse(Room room, RoomMember member, DateTimeOffset now) =>
        new(room.Id, room.Name, room.TimeZoneId, room.OwnerUserId, member.Id,
            room.OwnerUserId == member.UserId, room.ExpiresAtUtc, room.GetArchivedAtUtc(now),
            Membership: ToMembershipResponse(member), ArchiveOnExpiry: room.ArchiveOnExpiry);

    private static RoomMembershipResponse ToMembershipResponse(RoomMember member) =>
        new(member.Id, member.DisplayName, member.Color, member.IsFavourite, member.RowVersion);
}
