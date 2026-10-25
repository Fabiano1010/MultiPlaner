using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Rooms;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Common;
using MultiPlanerSharedModels.Contracts.Events;

namespace MultiPlanerAPI.Modules.Events;

public sealed class EventService(AppDbContext db, CurrentActor actor, TimeProvider clock,
    RoomAccessService access, RoomMutationService mutations)
{
    private sealed class EventRow
    {
        public CalendarEvent Event { get; init; } = null!;
        public Room Room { get; init; } = null!;
        public RoomMember Author { get; init; } = null!;
        public int ViewerMemberId { get; init; }
    }

    private IQueryable<EventRow> VisibleEvents(AppDbContext context)
    {
        var userId = actor.UserId;
        var guestId = actor.GuestSessionId;
        var now = clock.GetUtcNow();
        return from ev in context.CalendarEvents.AsNoTracking()
            join room in context.Rooms.AsNoTracking() on ev.RoomId equals room.Id
            join author in context.RoomMembers.AsNoTracking() on ev.AuthorMemberId equals author.Id
            join viewer in context.RoomMembers.AsNoTracking() on room.Id equals viewer.RoomId
            where viewer.LeftAtUtc == null &&
                  (userId != null && viewer.UserId == userId || guestId != null && viewer.GuestSessionId == guestId) &&
                  (room.ArchivedAtUtc != null || room.ExpiresAtUtc > now || room.ArchiveOnExpiry)
            select new EventRow { Event = ev, Room = room, Author = author, ViewerMemberId = viewer.Id };
    }

    public async Task<PagedResponse<EventResponse>> ListAsync(int? roomId, EventListRequest filter, CancellationToken ct)
    {
        if (roomId is { } id)
        {
            var (room, _) = await access.RequireMemberAsync(id, ct);
            access.RequireReadable(room);
        }
        var query = VisibleEvents(db);
        if ((roomId ?? filter.RoomId) is { } requestedRoom) query = query.Where(x => x.Event.RoomId == requestedRoom);
        if (roomId is not null && filter.RoomId is not null && roomId != filter.RoomId)
            throw ApiException.Validation(new Dictionary<string, string[]> { ["roomId"] = ["Room filter must match the route."] });
        if (roomId is null)
        {
            var now = clock.GetUtcNow();
            if (filter.Status == "active") query = query.Where(x => x.Room.ArchivedAtUtc == null && x.Room.ExpiresAtUtc > now);
            else if (filter.Status == "archived") query = query.Where(x => x.Room.ArchivedAtUtc != null ||
                x.Room.ArchiveOnExpiry && x.Room.ExpiresAtUtc <= now);
        }
        query = query.Where(x => x.Event.StartsAtUtc < filter.To && x.Event.EndsAtUtc > filter.From);
        if (filter.Category is { } category) query = query.Where(x => x.Event.Category == category);
        if (filter.AuthorMemberId is { } authorId) query = query.Where(x => x.Event.AuthorMemberId == authorId);
        if (filter.IsPinned is { } pinned) query = query.Where(x => x.Event.IsPinned == pinned);
        if (filter.IsHighPriority is { } priority) query = query.Where(x => x.Event.IsHighPriority == priority);
        var count = await query.CountAsync(ct);
        var offset = (long)(filter.Page - 1) * filter.PageSize;
        if (offset > int.MaxValue) return new([], count, filter.Page, filter.PageSize);
        var rows = await query.OrderBy(x => x.Event.StartsAtUtc).ThenBy(x => x.Event.Id)
            .Skip((int)offset).Take(filter.PageSize).ToListAsync(ct);
        var ids = rows.Select(x => x.Event.Id).ToArray();
        var participants = await db.EventParticipants.AsNoTracking().Where(p => ids.Contains(p.EventId))
            .OrderBy(p => p.MemberId).ToListAsync(ct);
        return new(rows.Select(x => Map(x, participants.Where(p => p.EventId == x.Event.Id)
            .Select(p => p.MemberId).ToArray())).ToArray(), count, filter.Page, filter.PageSize);
    }

    public async Task<EventResponse> GetAsync(int roomId, int eventId, CancellationToken ct)
    {
        var (room, _) = await access.RequireMemberAsync(roomId, ct);
        access.RequireReadable(room);
        return await ResponseAsync(db, roomId, eventId, ct);
    }

    public Task<EventResponse> CreateAsync(int roomId, CreateEventRequest request, CancellationToken ct) =>
        mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var member = await RequireWriterAsync(context, room, ct);
            var ev = new CalendarEvent
            {
                RoomId = roomId, AuthorMemberId = member.Id, Title = Title(request.Title),
                Description = EmptyToNull(request.Description), Category = request.Category,
                Color = request.Color?.ToUpperInvariant(), IsHighPriority = request.IsHighPriority, IsPinned = request.IsPinned
            };
            EventTime.Apply(ev, request.Time, room.TimeZoneId);
            var participants = await ValidateParticipantsAsync(context, roomId,
                request.ParticipantMemberIds ?? [member.Id], ct);
            context.CalendarEvents.Add(ev);
            await context.SaveChangesAsync(ct);
            context.EventParticipants.AddRange(participants.Select(id => new EventParticipant
                { EventId = ev.Id, RoomId = roomId, MemberId = id }));
            await context.SaveChangesAsync(ct);
            return await ResponseAsync(context, roomId, ev.Id, ct);
        }, ct);

    public Task<EventResponse> UpdateAsync(int roomId, int eventId, UpdateEventRequest request, CancellationToken ct) =>
        mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var member = await RequireWriterAsync(context, room, ct);
            var ev = await RequireEventAsync(context, roomId, eventId, request.RowVersion, ct);
            RoomAccessService.RequireOwnEntry(member.Id, ev.AuthorMemberId);
            if (request.Title is not null) ev.Title = Title(request.Title);
            if (request.Description is not null) ev.Description = EmptyToNull(request.Description);
            if (request.Color is not null) ev.Color = EmptyToNull(request.Color)?.ToUpperInvariant();
            if (request.Category is { } category) ev.Category = category;
            if (request.IsHighPriority is { } priority) ev.IsHighPriority = priority;
            if (request.IsPinned is { } pinned) ev.IsPinned = pinned;
            if (request.Time is not null) EventTime.Apply(ev, request.Time, room.TimeZoneId);
            if (request.ParticipantMemberIds is { } ids)
            {
                var validated = await ValidateParticipantsAsync(context, roomId, ids, ct);
                await context.EventParticipants.Where(p => p.EventId == eventId).ExecuteDeleteAsync(ct);
                context.EventParticipants.AddRange(validated.Select(id => new EventParticipant
                    { EventId = eventId, RoomId = roomId, MemberId = id }));
            }
            // Participant-only and no-op updates still advance the concurrency token.
            context.Entry(ev).Property(x => x.UpdatedAtUtc).IsModified = true;
            await context.SaveChangesAsync(ct);
            return await ResponseAsync(context, roomId, eventId, ct);
        }, ct);

    public Task<EventResponse> PinAsync(int roomId, int eventId, PinEventRequest request, CancellationToken ct) =>
        mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var member = await RequireWriterAsync(context, room, ct);
            var ev = await RequireEventAsync(context, roomId, eventId, request.RowVersion, ct);
            if (ev.AuthorMemberId != member.Id && room.OwnerUserId != actor.UserId)
                throw new ApiException(403, "event_pin_forbidden", "Only the author or room owner may pin this event.");
            ev.IsPinned = request.IsPinned;
            context.Entry(ev).Property(x => x.UpdatedAtUtc).IsModified = true;
            await context.SaveChangesAsync(ct);
            return await ResponseAsync(context, roomId, eventId, ct);
        }, ct);

    public Task DeleteAsync(int roomId, int eventId, EventVersionRequest request, CancellationToken ct) =>
        mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var member = await RequireWriterAsync(context, room, ct);
            var ev = await RequireEventAsync(context, roomId, eventId, request.RowVersion, ct);
            RoomAccessService.RequireOwnEntry(member.Id, ev.AuthorMemberId);
            await context.EventParticipants.Where(p => p.EventId == eventId).ExecuteDeleteAsync(ct);
            await context.EventAttachments.Where(a => a.EventId == eventId).ExecuteDeleteAsync(ct);
            context.CalendarEvents.Remove(ev);
            await context.SaveChangesAsync(ct);
            return true;
        }, ct);

    private async Task<RoomMember> RequireWriterAsync(AppDbContext context, Room room, CancellationToken ct)
    {
        var roomAccess = new RoomAccessService(context, actor, clock);
        var (_, member) = await roomAccess.RequireMemberAsync(room.Id, ct);
        roomAccess.RequireActive(room);
        return member;
    }

    private static async Task<CalendarEvent> RequireEventAsync(AppDbContext context, int roomId, int eventId,
        byte[] version, CancellationToken ct)
    {
        var ev = await context.CalendarEvents.SingleOrDefaultAsync(e => e.RoomId == roomId && e.Id == eventId, ct)
            ?? throw new ApiException(404, "event_not_found", "Event not found.");
        if (!ev.RowVersion.AsSpan().SequenceEqual(version))
            throw new ApiException(409, "concurrency_conflict", "Event changed. Reload and retry.");
        return ev;
    }

    private static async Task<int[]> ValidateParticipantsAsync(AppDbContext context, int roomId, int[] ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToArray();
        if (distinct.Any(id => id <= 0) || distinct.Length != ids.Length ||
            await context.RoomMembers.CountAsync(m => m.RoomId == roomId && m.LeftAtUtc == null && distinct.Contains(m.Id), ct) != distinct.Length)
            throw ApiException.Validation(new Dictionary<string, string[]>
                { ["participantMemberIds"] = ["Use distinct active members of this room."] });
        return distinct;
    }

    private async Task<EventResponse> ResponseAsync(AppDbContext context, int roomId, int eventId, CancellationToken ct)
    {
        var row = await VisibleEvents(context).SingleOrDefaultAsync(x => x.Event.RoomId == roomId && x.Event.Id == eventId, ct)
            ?? throw new ApiException(404, "event_not_found", "Event not found.");
        var participants = await context.EventParticipants.Where(p => p.EventId == eventId)
            .OrderBy(p => p.MemberId).Select(p => p.MemberId).ToArrayAsync(ct);
        return Map(row, participants);
    }

    private EventResponse Map(EventRow row, int[] participants)
    {
        var ev = row.Event;
        var active = row.Room.ArchivedAtUtc is null && row.Room.ExpiresAtUtc > clock.GetUtcNow();
        var own = row.ViewerMemberId == ev.AuthorMemberId;
        return new(ev.Id, ev.RoomId, row.Room.Name, row.Room.TimeZoneId,
            row.Room.GetArchivedAtUtc(clock.GetUtcNow()) is not null,
            ev.Title, ev.Description, ev.StartsAtUtc, ev.EndsAtUtc, ev.IsAllDay, ev.StartDate, ev.EndDateExclusive,
            ev.TimeZoneId, ev.Category, (ev.Color ?? row.Author.Color).ToUpperInvariant(), ev.Color,
            ev.IsHighPriority, ev.IsPinned,
            new(row.Author.Id, row.Author.DisplayName, row.Author.GuestSessionId != null,
                row.Author.UserId == row.Room.OwnerUserId, row.Author.LeftAtUtc != null), participants, ev.RowVersion,
            new(active && own, active && own, active && (own || row.Room.OwnerUserId == actor.UserId)));
    }

    private static string Title(string value) => string.IsNullOrWhiteSpace(value)
        ? throw ApiException.Validation(new Dictionary<string, string[]> { ["title"] = ["Title cannot be blank."] })
        : value.Trim();
    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
