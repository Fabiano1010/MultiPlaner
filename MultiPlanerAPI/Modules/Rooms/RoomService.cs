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
        if (zone is not null &&
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
        EventTypeService.AddDefaults(operationDb, room.Id);
        var token = InvitationTokens.Generate();
        var invitation = new RoomInvitation
        {
            RoomId = room.Id,
            TokenHash = InvitationTokens.Hash(token),
            ProtectedToken = tokens.Protect(token),
            ExpiresAtUtc = now.AddDays(7) < room.ExpiresAtUtc ? now.AddDays(7) : room.ExpiresAtUtc
        };
        operationDb.RoomInvitations.Add(invitation);
        await operationDb.SaveChangesAsync(cancellationToken);
        var response = ToResponse(room, member, clock.GetUtcNow(), user.DisplayName) with
        {
            InitialInvitation = links.CreateResponse(invitation, token)
        };
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
                join owner in db.Users.AsNoTracking() on room.OwnerUserId equals owner.Id
                join placement in db.UserFolderRooms.AsNoTracking().Where(p => p.OwnerUserId == userId)
                    on room.Id equals placement.RoomId into placements
                from placement in placements.DefaultIfEmpty()
                join visit in db.RoomVisits.AsNoTracking() on member.Id equals visit.MemberId into visits
                from visit in visits.DefaultIfEmpty()
                where member.LeftAtUtc == null && ((userId != null && member.UserId == userId) ||
                                                   (guestId != null && member.GuestSessionId == guestId)) &&
                      ((status == "active" && room.ArchivedAtUtc == null && room.ExpiresAtUtc > now) ||
                       (status == "archived" && (room.ArchivedAtUtc != null ||
                                                 (room.ArchiveOnExpiry && room.ExpiresAtUtc <= now))) ||
                       (status == "all" &&
                        (room.ArchivedAtUtc != null || room.ExpiresAtUtc > now || room.ArchiveOnExpiry)))
                orderby room.Id descending
                select new
                {
                    Room = room,
                    Member = member,
                    OwnerName = owner.DisplayName,
                    FolderId = placement == null ? (int?)null : placement.FolderId,
                    LastOpened = visit == null ? (DateTimeOffset?)null : visit.LastOpenedAtUtc
                })
            .ToListAsync(cancellationToken);
        return rooms.Select(item =>
            ToResponse(item.Room, item.Member, now, item.OwnerName, item.FolderId, item.LastOpened)).ToList();
    }

    public async Task<RoomResponse> GetAsync(int roomId, CancellationToken cancellationToken)
    {
        var (room, member) = await access.RequireMemberAsync(roomId, cancellationToken);
        access.RequireReadable(room);
        var ownerName = await db.Users.Where(u => u.Id == room.OwnerUserId)
            .Select(u => u.DisplayName).SingleAsync(cancellationToken);
        var folderId = await FolderIdOfAsync(db, roomId, cancellationToken);
        return ToResponse(room, member, clock.GetUtcNow(), ownerName, folderId);
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
                var member = await roomAccess.RequireMemberAsync(room, cancellationToken);
                roomAccess.RequireReadable(room);
                roomAccess.RequireActive(room);
                if (!member.RowVersion.AsSpan().SequenceEqual(request.RowVersion))
                {
                    throw new ApiException(409, "concurrency_conflict",
                        "Your room settings changed. Reload them and retry.");
                }

                context.RoomMembers.Attach(member);
                ApplyMembershipChanges(member, request);
                await context.SaveChangesAsync(cancellationToken);
                return ToMembershipResponse(member);
            }, cancellationToken);
        }

        var (currentRoom, currentMember) = await access.RequireMemberAsync(roomId, cancellationToken);
        access.RequireReadable(currentRoom);
        if (!currentMember.RowVersion.AsSpan().SequenceEqual(request.RowVersion))
        {
            throw new ApiException(409, "concurrency_conflict", "Your room settings changed. Reload them and retry.");
        }

        db.RoomMembers.Attach(currentMember);
        ApplyMembershipChanges(currentMember, request);
        await db.SaveChangesAsync(cancellationToken);
        return ToMembershipResponse(currentMember);
    }

    private static void ApplyMembershipChanges(RoomMember member, UpdateRoomMembershipRequest request)
    {
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
    }

    public async Task<PagedResponse<RoomMemberResponse>> ListMembersAsync(
        int roomId, PageRequest page, CancellationToken cancellationToken)
    {
        var (room, viewer) = await access.RequireMemberAsync(roomId, cancellationToken);
        access.RequireReadable(room);
        var query = db.RoomMembers.AsNoTracking().Where(m => m.RoomId == roomId && m.LeftAtUtc == null);
        var count = await query.CountAsync(cancellationToken);
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
                    Member = m, Avatar = u == null
                        ? null
                        : u.AvatarStorageKey != null
                            ? "/api/users/" + u.Id + "/avatar?v=" + u.AvatarStorageKey
                            : u.AvatarUrl
                })
            .Skip((int)offset).Take(page.PageSize).ToListAsync(cancellationToken);
        var owner = room.OwnerUserId == actor.UserId;
        var manage = owner || viewer.IsAdmin;
        var active = room.ArchivedAtUtc == null && room.ExpiresAtUtc > clock.GetUtcNow();
        var members = rows.Select(row =>
        {
            var m = row.Member;
            var targetOwner = m.UserId == room.OwnerUserId;
            return new RoomMemberResponse(m.Id, m.DisplayName, m.Color, m.GuestSessionId != null,
                targetOwner, m.CreatedAtUtc, m.IsAdmin, row.Avatar, m.RowVersion,
                active && manage && !targetOwner && (!m.IsAdmin || owner),
                active && manage && !targetOwner && !m.IsAdmin && m.UserId != null,
                active && owner && !targetOwner && m.IsAdmin);
        }).ToArray();
        return new PagedResponse<RoomMemberResponse>(members, count, page.Page, page.PageSize);
    }

    public async Task<RoomResponse> UpdateAsync(int roomId, UpdateRoomRequest request, CancellationToken ct)
    {
        var (updatedRoom, member) = await mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var roomAccess = new RoomAccessService(context, actor, clock);
            var currentMember = await roomAccess.RequireManagerAsync(room, ct);
            roomAccess.RequireActive(room);
            if ((request.TimeZoneId != null || request.ExpiresAtUtc != null) && room.OwnerUserId != actor.UserId)
            {
                throw new ApiException(403, "room_owner_required", "Only the owner may change time zone or expiry.");
            }

            if (!room.RowVersion.AsSpan().SequenceEqual(request.RowVersion))
            {
                throw new ApiException(409, "concurrency_conflict", "Room settings changed. Reload and retry.");
            }

            ValidateUpdate(request, room, clock.GetUtcNow());
            ApplyUpdate(request, room);

            await context.SaveChangesAsync(ct);
            return (room, currentMember);
        }, ct);

        var ownerName = await db.Users.AsNoTracking().Where(u => u.Id == updatedRoom.OwnerUserId)
            .Select(u => u.DisplayName).SingleAsync(ct);
        var folderId = await FolderIdOfAsync(db, roomId, ct);

        return ToResponse(updatedRoom, member, clock.GetUtcNow(), ownerName, folderId);
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

            var roomAccess = new RoomAccessService(context, actor, clock);
            roomAccess.RequireActive(room);
            room.ArchivedAtUtc = now;
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task DeleteAsync(int roomId, CancellationToken cancellationToken)
    {
        var ownerId = actor.RequireUserId();
        var coverKey = await mutations.ExecuteInTransactionAsync(
            operationDb => DeleteRoomAsync(operationDb, roomId, ownerId, cancellationToken),
            cancellationToken);
        if (coverKey is not null)
        {
            await covers.DeleteAsync(coverKey);
        }
    }

    private static async Task<string?> DeleteRoomAsync(AppDbContext operationDb, int roomId, int ownerId,
        CancellationToken cancellationToken)
    {
        var room = await RoomMutationService.LockAsync(operationDb, roomId, cancellationToken);
        if (room.OwnerUserId != ownerId)
        {
            throw new ApiException(403, "room_owner_required", "Only the room owner may manage it.");
        }

        var coverKey = room.CoverStorageKey;
        await DeleteRoomContentsAsync(operationDb, roomId, cancellationToken);
        await operationDb.Rooms.Where(r => r.Id == roomId)
            .ExecuteDeleteAsync(cancellationToken);
        return coverKey;
    }

    private Task<int?> FolderIdOfAsync(AppDbContext context, int roomId, CancellationToken ct)
    {
        var userId = actor.UserId;
        return context.UserFolderRooms.AsNoTracking()
            .Where(p => p.RoomId == roomId && p.OwnerUserId == userId)
            .Select(p => (int?)p.FolderId)
            .SingleOrDefaultAsync(ct);
    }

    public async Task RecordVisitAsync(int roomId, CancellationToken ct)
    {
        var (room, member) = await access.RequireMemberAsync(roomId, ct);
        access.RequireReadable(room);

        var now = clock.GetUtcNow();
        var threshold = now.AddMinutes(-1);

        var updated = await db.RoomVisits
            .Where(v => v.MemberId == member.Id && v.LastOpenedAtUtc < threshold)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.LastOpenedAtUtc, now), ct);
        if (updated > 0)
        {
            return;
        }

        if (await db.RoomVisits.AnyAsync(v => v.MemberId == member.Id, ct))
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
            // already exists
        }
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
        await operationDb.RoomEventTypes.Where(t => t.RoomId == roomId).ExecuteDeleteAsync(cancellationToken);
        await operationDb.RoomInvitations.Where(i => i.RoomId == roomId)
            .ExecuteDeleteAsync(cancellationToken);
        await operationDb.UserFolderRooms.Where(a => a.RoomId == roomId)
            .ExecuteDeleteAsync(cancellationToken);
        await operationDb.OutboxMessages.Where(m => m.RoomId == roomId)
            .ExecuteDeleteAsync(cancellationToken);
        var pollIds = await RoomMutationService.LockRoomPollsAsync(operationDb, roomId, cancellationToken);
        if (pollIds.Length > 0)
        {
            await operationDb.PollVotes.Where(v => pollIds.Contains(v.PollId)).ExecuteDeleteAsync(cancellationToken);
            await operationDb.PollOptions.Where(o => pollIds.Contains(o.PollId)).ExecuteDeleteAsync(cancellationToken);
            await operationDb.Polls.Where(p => p.RoomId == roomId).ExecuteDeleteAsync(cancellationToken);
        }

        var memberIds = operationDb.RoomMembers.Where(m => m.RoomId == roomId).Select(m => m.Id);
        await operationDb.RoomVisits.Where(v => memberIds.Contains(v.MemberId)).ExecuteDeleteAsync(cancellationToken);
        await operationDb.RoomMembers.Where(m => m.RoomId == roomId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    internal static RoomResponse ToResponse(Room room, RoomMember member, DateTimeOffset now,
        string? ownerDisplayName = null, int? folderId = null, DateTimeOffset? lastOpenedAtUtc = null)
    {
        return new RoomResponse(room.Id, room.Name, room.TimeZoneId, room.OwnerUserId, member.Id,
            room.OwnerUserId == member.UserId, room.ExpiresAtUtc, room.GetArchivedAtUtc(now),
            Membership: ToMembershipResponse(member), ArchiveOnExpiry: room.ArchiveOnExpiry,
            OwnerDisplayName: ownerDisplayName, RowVersion: room.RowVersion,
            HasCover: room.CoverStorageKey is not null, IsAdmin: member.IsAdmin,
            FolderId: folderId, LastOpenedAtUtc: lastOpenedAtUtc);
    }

    private static RoomMembershipResponse ToMembershipResponse(RoomMember member)
    {
        return new RoomMembershipResponse(member.Id, member.DisplayName, member.Color, member.IsFavourite,
            member.RowVersion,
            member.NotificationsMuted);
    }
}