using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Common;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class RoomService(
    AppDbContext db,
    RoomMutationService mutations,
    CurrentActor actor,
    TimeProvider clock,
    InvitationLinkBuilder links,
    RoomAccessService access,
    IDataProtectionProvider dataProtection,
    RoomCoverStorage covers)
{
    private readonly IDataProtector protector = InvitationTokens.CreateProtector(dataProtection);
    public async Task<RoomResponse> CreateAsync(CreateRoomRequest request, CancellationToken ct)
    {
        var userId = actor.RequireUserId();
        var now = clock.GetUtcNow();
        var expiry = request.ExpiresAtUtc ?? now.AddMonths(3);
        ValidateRoomFields(request.Name, true, request.TimeZoneId, expiry, now, now.AddDays(365),
            "Room expiry must be UTC, in the future and within one year.");

        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
        return await mutations.ExecuteInTransactionAsync(
            operationDb => CreateRoomAsync(operationDb, request, user, now, expiry, ct), ct);
    }

    public async Task<IReadOnlyList<RoomResponse>> ListAsync(string status, CancellationToken ct)
    {
        if (status is not ("active" or "archived" or "all"))
        {
            throw ApiException.Validation("status", "Use active, archived or all.");
        }

        var userId = actor.UserId;
        var guestId = actor.GuestSessionId;
        var now = clock.GetUtcNow();

        var query = from member in db.RoomMembers.AsNoTracking()
            join room in db.Rooms.AsNoTracking() on member.RoomId equals room.Id
            join owner in db.Users.AsNoTracking() on room.OwnerUserId equals owner.Id
            join placement in db.UserFolderRooms.AsNoTracking().Where(p => p.OwnerUserId == userId)
                on room.Id equals placement.RoomId into placements
            from placement in placements.DefaultIfEmpty()
            join visit in db.RoomVisits.AsNoTracking() on member.Id equals visit.MemberId into visits
            from visit in visits.DefaultIfEmpty()
            where member.LeftAtUtc == null &&
                  ((userId != null && member.UserId == userId) || (guestId != null && member.GuestSessionId == guestId))
            select new
            {
                Room = room, Member = member, OwnerName = owner.DisplayName,
                FolderId = placement == null ? (int?)null : placement.FolderId,
                LastOpened = visit == null ? (DateTimeOffset?)null : visit.LastOpenedAtUtc
            };

        query = status switch
        {
            "active" => query.Where(x => x.Room.ArchivedAtUtc == null && x.Room.ExpiresAtUtc > now),
            "archived" => query.Where(x =>
                x.Room.ArchivedAtUtc != null || (x.Room.ArchiveOnExpiry && x.Room.ExpiresAtUtc <= now)),
            _ => query.Where(x => x.Room.ArchivedAtUtc != null || x.Room.ExpiresAtUtc > now || x.Room.ArchiveOnExpiry)
        };

        var rooms = await query.OrderByDescending(x => x.Room.Id).ToListAsync(ct);
        return rooms.Select(x => ToResponse(x.Room, x.Member, now, x.OwnerName, x.FolderId, x.LastOpened)).ToList();
    }

    public async Task<RoomResponse> GetAsync(int roomId, CancellationToken ct)
    {
        var (room, member) = await access.RequireMemberAsync(roomId, ct);
        access.RequireReadable(room);
        var ownerName = await db.Users.Where(u => u.Id == room.OwnerUserId).Select(u => u.DisplayName).SingleAsync(ct);
        var folderId = await FolderIdOfAsync(roomId, ct);
        return ToResponse(room, member, clock.GetUtcNow(), ownerName, folderId);
    }

    public async Task<RoomMembershipResponse> GetMembershipAsync(int roomId, CancellationToken ct)
    {
        var (room, member) = await access.RequireMemberAsync(roomId, ct);
        access.RequireReadable(room);
        return ToMembershipResponse(member);
    }

    public async Task<RoomMembershipResponse> UpdateMembershipAsync(
        int roomId, UpdateRoomMembershipRequest request, CancellationToken ct)
    {
        var errors = LocaleValidation.GetErrors(request.DisplayName, null, null);
        if (errors.Count != 0)
        {
            throw ApiException.Validation(errors);
        }

        // Public fields (DisplayName, Color) must serialize with room archival/deletion.
        if (request.DisplayName is not null || request.Color is not null)
        {
            return await mutations.ExecuteAsMemberAsync(roomId,
                (context, _, member) => SaveMembershipAsync(context, member, request, ct), ct);
        }

        var (currentRoom, currentMember) = await access.RequireMemberAsync(roomId, ct);
        access.RequireReadable(currentRoom);
        return await SaveMembershipAsync(db, currentMember, request, ct);
    }

    private static async Task<RoomMembershipResponse> SaveMembershipAsync(
        AppDbContext context, RoomMember member, UpdateRoomMembershipRequest request, CancellationToken ct)
    {
        ApiException.RequireVersion(member.RowVersion, request.RowVersion,
            "Your room settings changed. Reload them and retry.");

        context.RoomMembers.Attach(member);
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

        if (request.NotificationsMuted is { } muted)
        {
            member.NotificationsMuted = muted;
        }

        await context.SaveChangesAsync(ct);
        return ToMembershipResponse(member);
    }

    public async Task<PagedResponse<RoomMemberResponse>> ListMembersAsync(int roomId, PageRequest page,
        CancellationToken ct)
    {
        var (room, viewer) = await access.RequireMemberAsync(roomId, ct);
        access.RequireReadable(room);

        var query = db.RoomMembers.AsNoTracking().Where(m => m.RoomId == roomId && m.LeftAtUtc == null);
        var count = await query.CountAsync(ct);
        var offset = (long)(page.Page - 1) * page.PageSize;
        if (offset > int.MaxValue)
        {
            return new PagedResponse<RoomMemberResponse>([], count, page.Page, page.PageSize);
        }

        var rows = await (from m in query
                join u in db.Users on m.UserId equals u.Id into users
                from u in users.DefaultIfEmpty()
                orderby m.Id
                select new
                {
                    Member = m,
                    AvatarKey = u == null ? null : u.AvatarStorageKey,
                    AvatarUrl = u == null ? null : u.AvatarUrl
                })
            .Skip((int)offset).Take(page.PageSize).ToListAsync(ct);

        var owner = room.OwnerUserId == actor.UserId;
        var manage = owner || viewer.IsAdmin;
        var active = room.ArchivedAtUtc == null && room.ExpiresAtUtc > clock.GetUtcNow();
        var members = rows.Select(row =>
        {
            var m = row.Member;
            var targetOwner = m.UserId == room.OwnerUserId;
            var avatar = AvatarService.BuildUrl(m.UserId, row.AvatarKey, row.AvatarUrl);
            return new RoomMemberResponse(
                m.Id, m.DisplayName, m.Color, m.GuestSessionId != null, targetOwner, m.CreatedAtUtc, m.IsAdmin,
                avatar, m.RowVersion,
                active && manage && !targetOwner && (!m.IsAdmin || owner),
                active && manage && !targetOwner && !m.IsAdmin && m.UserId != null,
                active && owner && !targetOwner && m.IsAdmin);
        }).ToArray();

        return new PagedResponse<RoomMemberResponse>(members, count, page.Page, page.PageSize);
    }

    public async Task<RoomResponse> UpdateAsync(int roomId, UpdateRoomRequest request, CancellationToken ct)
    {
        var result = await mutations.ExecuteAsManagerAsync(roomId, async (context, room, member) =>
        {
            if ((request.TimeZoneId is not null || request.ExpiresAtUtc is not null) &&
                room.OwnerUserId != actor.UserId)
            {
                throw new ApiException(403, "room_owner_required", "Only the owner may change time zone or expiry.");
            }

            ApiException.RequireVersion(room.RowVersion, request.RowVersion, "Room settings changed. Reload and retry.");

            ValidateRoomFields(request.Name, false, request.TimeZoneId, request.ExpiresAtUtc, clock.GetUtcNow(),
                room.CreatedAtUtc.AddDays(365),
                "Room expiry must be UTC, in the future and within one year of creation.");
            if (request.Name is not null)
            {
                room.Name = request.Name.Trim();
            }

            if (request.TimeZoneId is not null)
            {
                room.TimeZoneId = request.TimeZoneId;
            }

            if (request.ExpiresAtUtc is { } expiry)
            {
                room.ExpiresAtUtc = expiry;
            }

            await context.SaveChangesAsync(ct);
            return (Room: room, Member: member);
        }, ct);

        var ownerName = await db.Users.AsNoTracking().Where(u => u.Id == result.Room.OwnerUserId)
            .Select(u => u.DisplayName).SingleAsync(ct);
        var folderId = await FolderIdOfAsync(roomId, ct);
        return ToResponse(result.Room, result.Member, clock.GetUtcNow(), ownerName, folderId);
    }

    public async Task ArchiveAsync(int roomId, CancellationToken ct)
    {
        var userId = actor.RequireUserId();
        await mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            if (room.OwnerUserId != userId)
            {
                throw new ApiException(403, "room_owner_required", "Only the room owner may manage it.");
            }

            var now = clock.GetUtcNow();
            if (room.GetArchivedAtUtc(now) is not null)
            {
                return true;
            }

            access.RequireActive(room);
            room.ArchivedAtUtc = now;
            await context.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    public async Task DeleteAsync(int roomId, CancellationToken ct)
    {
        var ownerId = actor.RequireUserId();
        var coverKey = await mutations.ExecuteAsync(roomId,
            (operationDb, room) => DeleteRoomAsync(operationDb, room, ownerId, ct), ct);
        covers.Delete(coverKey);
    }

    public async Task RecordVisitAsync(int roomId, CancellationToken ct)
    {
        var (room, member) = await access.RequireMemberAsync(roomId, ct);
        access.RequireReadable(room);

        var now = clock.GetUtcNow();
        var threshold = now.AddMinutes(-1);
        var updated = await db.RoomVisits.Where(v => v.MemberId == member.Id && v.LastOpenedAtUtc < threshold)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.LastOpenedAtUtc, now), ct);
        if (updated > 0 || await db.RoomVisits.AnyAsync(v => v.MemberId == member.Id, ct))
        {
            return;
        }

        db.RoomVisits.Add(new RoomVisit { MemberId = member.Id, LastOpenedAtUtc = now });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another concurrent request created the visit row first.
        }
    }

    internal static RoomResponse ToResponse(
        Room room, RoomMember member, DateTimeOffset now,
        string? ownerDisplayName = null, int? folderId = null, DateTimeOffset? lastOpenedAtUtc = null)
    {
        return new RoomResponse(room.Id, room.Name, room.TimeZoneId, room.OwnerUserId, member.Id,
            room.OwnerUserId == member.UserId,
            room.ExpiresAtUtc, room.GetArchivedAtUtc(now), Membership: ToMembershipResponse(member),
            ArchiveOnExpiry: room.ArchiveOnExpiry, OwnerDisplayName: ownerDisplayName, RowVersion: room.RowVersion,
            HasCover: room.CoverStorageKey is not null, IsAdmin: member.IsAdmin, FolderId: folderId,
            LastOpenedAtUtc: lastOpenedAtUtc);
    }

    private static RoomMembershipResponse ToMembershipResponse(RoomMember member)
    {
        return new RoomMembershipResponse(member.Id, member.DisplayName, member.Color, member.IsFavourite,
            member.RowVersion,
            member.NotificationsMuted);
    }

    private static void ValidateRoomFields(
        string? name, bool nameRequired, string? timeZoneId,
        DateTimeOffset? expiry, DateTimeOffset now, DateTimeOffset maxExpiry, string expiryError)
    {
        var errors = new Dictionary<string, string[]>();
        if ((nameRequired && string.IsNullOrWhiteSpace(name)) || (name is not null && name.Trim().Length < 2))
        {
            errors["name"] = ["Room name must contain at least two non-whitespace characters."];
        }

        if (timeZoneId is not null &&
            LocaleValidation.GetErrors(null, null, timeZoneId).TryGetValue("timeZoneId", out var zoneErrors))
        {
            errors["timeZoneId"] = zoneErrors;
        }

        if (expiry is { } exp && (exp.Offset != TimeSpan.Zero || exp <= now || exp > maxExpiry))
        {
            errors["expiresAtUtc"] = [expiryError];
        }

        if (errors.Count != 0)
        {
            throw ApiException.Validation(errors);
        }
    }

    private async Task<RoomResponse> CreateRoomAsync(
        AppDbContext operationDb, CreateRoomRequest request, ApplicationUser user, DateTimeOffset now,
        DateTimeOffset expiry, CancellationToken ct)
    {
        var room = new Room
        {
            OwnerUserId = user.Id, Name = request.Name.Trim(), TimeZoneId = request.TimeZoneId ?? user.TimeZoneId,
            ExpiresAtUtc = expiry, ArchiveOnExpiry = request.ArchiveOnExpiry
        };
        operationDb.Rooms.Add(room);
        await operationDb.SaveChangesAsync(ct);

        var member = new RoomMember { RoomId = room.Id, UserId = user.Id, DisplayName = user.DisplayName };
        operationDb.RoomMembers.Add(member);
        EventTypeService.AddDefaults(operationDb, room.Id);

        var token = InvitationTokens.Generate();
        var weekExpiry = now.AddDays(7);
        var invitation = new RoomInvitation
        {
            RoomId = room.Id, TokenHash = InvitationTokens.Hash(token), ProtectedToken = protector.Protect(token),
            ExpiresAtUtc = weekExpiry < room.ExpiresAtUtc ? weekExpiry : room.ExpiresAtUtc
        };
        operationDb.RoomInvitations.Add(invitation);
        await operationDb.SaveChangesAsync(ct);

        return ToResponse(room, member, clock.GetUtcNow(), user.DisplayName) with
        {
            InitialInvitation = links.CreateResponse(invitation, token)
        };
    }

    private static async Task<string?> DeleteRoomAsync(AppDbContext operationDb, Room room, int ownerId,
        CancellationToken ct)
    {
        if (room.OwnerUserId != ownerId)
        {
            throw new ApiException(403, "room_owner_required", "Only the room owner may manage it.");
        }

        var coverKey = room.CoverStorageKey;
        await DeleteRoomContentsAsync(operationDb, room.Id, ct);
        await operationDb.Rooms.Where(r => r.Id == room.Id).ExecuteDeleteAsync(ct);
        return coverKey;
    }

    private Task<int?> FolderIdOfAsync(int roomId, CancellationToken ct)
    {
        return db.UserFolderRooms.AsNoTracking()
            .Where(p => p.RoomId == roomId && p.OwnerUserId == actor.UserId)
            .Select(p => (int?)p.FolderId).SingleOrDefaultAsync(ct);
    }

    private static async Task DeleteRoomContentsAsync(AppDbContext operationDb, int roomId, CancellationToken ct)
    {
        await operationDb.EventParticipants.Where(p => p.RoomId == roomId).ExecuteDeleteAsync(ct);
        await operationDb.CalendarEvents.Where(e => e.RoomId == roomId).ExecuteDeleteAsync(ct);
        await operationDb.RoomEventTypes.Where(t => t.RoomId == roomId).ExecuteDeleteAsync(ct);
        await operationDb.RoomInvitations.Where(i => i.RoomId == roomId).ExecuteDeleteAsync(ct);
        await operationDb.UserFolderRooms.Where(a => a.RoomId == roomId).ExecuteDeleteAsync(ct);

        var pollIds = await RoomMutationService.LockRoomPollsAsync(operationDb, roomId, ct);
        if (pollIds.Length > 0)
        {
            await operationDb.PollVotes.Where(v => pollIds.Contains(v.PollId)).ExecuteDeleteAsync(ct);
            await operationDb.PollOptions.Where(o => pollIds.Contains(o.PollId)).ExecuteDeleteAsync(ct);
            await operationDb.Polls.Where(p => p.RoomId == roomId).ExecuteDeleteAsync(ct);
        }

        var memberIds = operationDb.RoomMembers.Where(m => m.RoomId == roomId).Select(m => m.Id);
        await operationDb.RoomVisits.Where(v => memberIds.Contains(v.MemberId)).ExecuteDeleteAsync(ct);
        await operationDb.RoomMembers.Where(m => m.RoomId == roomId).ExecuteDeleteAsync(ct);
    }
}