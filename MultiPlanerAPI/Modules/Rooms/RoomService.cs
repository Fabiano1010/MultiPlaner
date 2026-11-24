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
    DbContextOptions<AppDbContext> dbOptions,
    RoomMutationService mutations,
    CurrentActor actor,
    TimeProvider clock,
    InvitationLinkBuilder links,
    RoomAccessService access,
    InvitationTokenProtector tokens,
    IRoomCoverStorage covers)
{
    public async Task<RoomResponse> CreateAsync(CreateRoomRequest request, CancellationToken ct)
    {
        var userId = actor.RequireUserId();
        var now = clock.GetUtcNow();
        var expiry = request.ExpiresAtUtc ?? now.AddMonths(3);
        ValidateCreate(request, now, expiry);

        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(() => CreateRoomAsync(request, user, now, expiry, ct));
    }

    public async Task<IReadOnlyList<RoomResponse>> ListAsync(string status, CancellationToken ct)
    {
        if (status is not ("active" or "archived" or "all"))
        {
            throw ApiException.Validation(new Dictionary<string, string[]>
                { ["status"] = ["Use active, archived or all."] });
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
        var folderId = await FolderIdOfAsync(db, roomId, ct);
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
            return await mutations.ExecuteAsync(roomId, async (context, room) =>
            {
                var roomAccess = new RoomAccessService(context, actor, clock);
                var member = await roomAccess.RequireMemberAsync(room, ct);
                roomAccess.RequireReadable(room);
                roomAccess.RequireActive(room);
                return await SaveMembershipAsync(context, member, request, ct);
            }, ct);
        }

        var (currentRoom, currentMember) = await access.RequireMemberAsync(roomId, ct);
        access.RequireReadable(currentRoom);
        return await SaveMembershipAsync(db, currentMember, request, ct);
    }

    private static async Task<RoomMembershipResponse> SaveMembershipAsync(
        AppDbContext context, RoomMember member, UpdateRoomMembershipRequest request, CancellationToken ct)
    {
        if (!member.RowVersion.AsSpan().SequenceEqual(request.RowVersion))
        {
            throw new ApiException(409, "concurrency_conflict", "Your room settings changed. Reload them and retry.");
        }

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
            var avatar = m.UserId is null ? null
                : row.AvatarKey is not null ? $"/api/users/{m.UserId}/avatar?v={row.AvatarKey}"
                : row.AvatarUrl;
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
        var result = await mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var roomAccess = new RoomAccessService(context, actor, clock);
            var member = await roomAccess.RequireManagerAsync(room, ct);
            roomAccess.RequireActive(room);
            if ((request.TimeZoneId is not null || request.ExpiresAtUtc is not null) &&
                room.OwnerUserId != actor.UserId)
            {
                throw new ApiException(403, "room_owner_required", "Only the owner may change time zone or expiry.");
            }

            if (!room.RowVersion.AsSpan().SequenceEqual(request.RowVersion))
            {
                throw new ApiException(409, "concurrency_conflict", "Room settings changed. Reload and retry.");
            }

            ValidateUpdate(request, room, clock.GetUtcNow());
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
        var folderId = await FolderIdOfAsync(db, roomId, ct);
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

            new RoomAccessService(context, actor, clock).RequireActive(room);
            room.ArchivedAtUtc = now;
            await context.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    public async Task DeleteAsync(int roomId, CancellationToken ct)
    {
        var ownerId = actor.RequireUserId();
        var coverKey =
            await mutations.ExecuteInTransactionAsync(operationDb => DeleteRoomAsync(operationDb, roomId, ownerId, ct),
                ct);
        if (coverKey is not null)
        {
            await covers.DeleteAsync(coverKey);
        }
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

    private static void ValidateCreate(CreateRoomRequest request, DateTimeOffset now, DateTimeOffset expiry)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length < 2)
        {
            errors["name"] = ["Room name must contain at least two non-whitespace characters."];
        }

        if (request.TimeZoneId is { } zone &&
            LocaleValidation.GetErrors(null, null, zone).TryGetValue("timeZoneId", out var zoneErrors))
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

    private static void ValidateUpdate(UpdateRoomRequest request, Room room, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Name is not null && request.Name.Trim().Length < 2)
        {
            errors["name"] = ["Room name must contain at least two non-whitespace characters."];
        }

        if (request.TimeZoneId is not null &&
            LocaleValidation.GetErrors(null, null, request.TimeZoneId).TryGetValue("timeZoneId", out var zoneErrors))
        {
            errors["timeZoneId"] = zoneErrors;
        }

        if (request.ExpiresAtUtc is { } expiry &&
            (expiry.Offset != TimeSpan.Zero || expiry <= now || expiry > room.CreatedAtUtc.AddDays(365)))
        {
            errors["expiresAtUtc"] = ["Room expiry must be UTC, in the future and within one year of creation."];
        }

        if (errors.Count != 0)
        {
            throw ApiException.Validation(errors);
        }
    }

    private async Task<RoomResponse> CreateRoomAsync(
        CreateRoomRequest request, ApplicationUser user, DateTimeOffset now, DateTimeOffset expiry,
        CancellationToken ct)
    {
        await using var operationDb = new AppDbContext(dbOptions, clock);
        await using var transaction = await operationDb.Database.BeginTransactionAsync(ct);

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
            RoomId = room.Id, TokenHash = InvitationTokens.Hash(token), ProtectedToken = tokens.Protect(token),
            ExpiresAtUtc = weekExpiry < room.ExpiresAtUtc ? weekExpiry : room.ExpiresAtUtc
        };
        operationDb.RoomInvitations.Add(invitation);
        await operationDb.SaveChangesAsync(ct);

        var response = ToResponse(room, member, clock.GetUtcNow(), user.DisplayName) with
        {
            InitialInvitation = links.CreateResponse(invitation, token)
        };
        await transaction.CommitAsync(ct);
        return response;
    }

    private static async Task<string?> DeleteRoomAsync(AppDbContext operationDb, int roomId, int ownerId,
        CancellationToken ct)
    {
        var room = await RoomMutationService.LockAsync(operationDb, roomId, ct);
        if (room.OwnerUserId != ownerId)
        {
            throw new ApiException(403, "room_owner_required", "Only the room owner may manage it.");
        }

        var coverKey = room.CoverStorageKey;
        await DeleteRoomContentsAsync(operationDb, roomId, ct);
        await operationDb.Rooms.Where(r => r.Id == roomId).ExecuteDeleteAsync(ct);
        return coverKey;
    }

    private Task<int?> FolderIdOfAsync(AppDbContext context, int roomId, CancellationToken ct)
    {
        return context.UserFolderRooms.AsNoTracking()
            .Where(p => p.RoomId == roomId && p.OwnerUserId == actor.UserId)
            .Select(p => (int?)p.FolderId).SingleOrDefaultAsync(ct);
    }

    private static async Task DeleteRoomContentsAsync(AppDbContext operationDb, int roomId, CancellationToken ct)
    {
        await operationDb.EventParticipants.Where(p => p.RoomId == roomId).ExecuteDeleteAsync(ct);
        var eventIds = operationDb.CalendarEvents.Where(e => e.RoomId == roomId).Select(e => e.Id);
        await operationDb.EventAttachments.Where(a => eventIds.Contains(a.EventId)).ExecuteDeleteAsync(ct);
        await operationDb.AvailabilityIntervals.Where(a => a.RoomId == roomId).ExecuteDeleteAsync(ct);
        await operationDb.ChatMessages.Where(m => m.RoomId == roomId).ExecuteDeleteAsync(ct);
        await operationDb.RoomActivities.Where(a => a.RoomId == roomId).ExecuteDeleteAsync(ct);
        await operationDb.CalendarEvents.Where(e => e.RoomId == roomId).ExecuteDeleteAsync(ct);
        await operationDb.RoomEventTypes.Where(t => t.RoomId == roomId).ExecuteDeleteAsync(ct);
        await operationDb.RoomInvitations.Where(i => i.RoomId == roomId).ExecuteDeleteAsync(ct);
        await operationDb.UserFolderRooms.Where(a => a.RoomId == roomId).ExecuteDeleteAsync(ct);
        await operationDb.OutboxMessages.Where(m => m.RoomId == roomId).ExecuteDeleteAsync(ct);

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