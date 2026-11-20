using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Rooms;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Common;
using MultiPlanerSharedModels.Contracts.Events;

namespace MultiPlanerAPI.Modules.Events;

public sealed class EventService(
    AppDbContext db,
    CurrentActor actor,
    TimeProvider clock,
    RoomAccessService access,
    RoomMutationService mutations)
{
    private const int MaxSeriesPerRoom = 50, MaxOccurrencesPerSeries = 400, MaxWindowDays = 400, MaxSeriesPerQuery = 500, MaxMergedRows = 5000;
    private sealed class EventRow
    {
        public CalendarEvent Event { get; init; } = null!;
        public RoomEventType Type { get; init; } = null!;
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
            join type in context.RoomEventTypes.AsNoTracking() on ev.EventTypeId equals type.Id
            join room in context.Rooms.AsNoTracking() on ev.RoomId equals room.Id
            join author in context.RoomMembers.AsNoTracking() on ev.AuthorMemberId equals author.Id
            join viewer in context.RoomMembers.AsNoTracking() on room.Id equals viewer.RoomId
            where viewer.LeftAtUtc == null &&
                  ((userId != null && viewer.UserId == userId) ||
                   (guestId != null && viewer.GuestSessionId == guestId)) &&
                  (room.ArchivedAtUtc != null || room.ExpiresAtUtc > now || room.ArchiveOnExpiry)
            select new EventRow { Event = ev, Type = type, Room = room, Author = author, ViewerMemberId = viewer.Id };
    }

    public async Task<PagedResponse<EventResponse>> ListAsync(int? roomId, EventListRequest filter, CancellationToken ct)
    {
        if (roomId is { } id)
        {
            var (room, _) = await access.RequireMemberAsync(id, ct);
            access.RequireReadable(room);
        }

        var query = VisibleEvents(db);
        if ((roomId ?? filter.RoomId) is { } requestedRoom)
        {
            query = query.Where(x => x.Event.RoomId == requestedRoom);
        }

        if (roomId is not null && filter.RoomId is not null && roomId != filter.RoomId)
        {
            throw ApiException.Validation(new Dictionary<string, string[]>
                { ["roomId"] = ["Room filter must match the route."] });
        }

        if (roomId is null)
        {
            var now = clock.GetUtcNow();
            if (filter.Status == "active")
            {
                query = query.Where(x => x.Room.ArchivedAtUtc == null && x.Room.ExpiresAtUtc > now);
            }
            else if (filter.Status == "archived")
            {
                query = query.Where(x => x.Room.ArchivedAtUtc != null ||
                                         (x.Room.ArchiveOnExpiry && x.Room.ExpiresAtUtc <= now));
            }
        }

        if (filter.EventTypeId is { } typeId)
        {
            query = query.Where(x => x.Event.EventTypeId == typeId);
        }

        if (filter.AuthorMemberId is { } authorId)
        {
            query = query.Where(x => x.Event.AuthorMemberId == authorId);
        }

        if (filter.IsPinned is { } pinned)
        {
            query = query.Where(x => x.Event.IsPinned == pinned);
        }

        if (filter.IsHighPriority is { } priority)
        {
            query = query.Where(x => x.Event.IsHighPriority == priority);
        }

        var from = filter.From!.Value;
        var to = filter.To!.Value;
        
        var singlesQuery = query.Where(x => x.Event.Recurrence == RecurrenceFrequency.None
                                            && x.Event.StartsAtUtc < to && x.Event.EndsAtUtc > from);
        
        var expandTo = to < from.AddDays(MaxWindowDays) ? to : from.AddDays(MaxWindowDays);
        var fromDay = DateOnly.FromDateTime(from.UtcDateTime.AddDays(-1));
        var series = await query
            .Where(x => x.Event.Recurrence != RecurrenceFrequency.None
                        && x.Event.StartsAtUtc < expandTo
                        && (x.Event.RecurrenceUntil == null || x.Event.RecurrenceUntil >= fromDay))
            .Take(MaxSeriesPerQuery)
            .ToListAsync(ct);
        
        if (series.Count == 0)
        {
            var count = await singlesQuery.CountAsync(ct);
            var offset = (long)(filter.Page - 1) * filter.PageSize;
            if (offset > int.MaxValue)
            {
                return new PagedResponse<EventResponse>([], count, filter.Page, filter.PageSize);
            }

            var rows = await singlesQuery.OrderBy(x => x.Event.StartsAtUtc).ThenBy(x => x.Event.Id)
                .Skip((int)offset).Take(filter.PageSize).ToListAsync(ct);
            var ids = rows.Select(x => x.Event.Id).ToArray();
            var participants = await db.EventParticipants.AsNoTracking().Where(p => ids.Contains(p.EventId))
                .OrderBy(p => p.MemberId).ToListAsync(ct);
            return new PagedResponse<EventResponse>(rows.Select(x => Map(x, participants
                .Where(p => p.EventId == x.Event.Id).Select(p => p.MemberId).ToArray())).ToArray(),
                count, filter.Page, filter.PageSize);
        }
        
        var singles = await singlesQuery.OrderBy(x => x.Event.StartsAtUtc).ThenBy(x => x.Event.Id)
            .Take(MaxMergedRows).ToListAsync(ct);

        var zones = new Dictionary<string, TimeZoneInfo>();
        TimeZoneInfo Zone(string id) =>
            zones.TryGetValue(id, out var z) ? z : zones[id] = TimeZoneInfo.FindSystemTimeZoneById(id);

        var seriesIds = series.Select(x => x.Event.Id).ToArray();
        var skipped = (await db.EventOccurrenceExceptions.AsNoTracking()
                .Where(x => seriesIds.Contains(x.EventId)).ToListAsync(ct))
            .Select(x => (x.EventId, x.OccurrenceDate)).ToHashSet();

        var items = singles
            .Select(r => (Row: r, Occ: (RecurrenceExpander.Occurrence?)null, Start: r.Event.StartsAtUtc))
            .ToList();

        foreach (var s in series)
        {
            foreach (var o in RecurrenceExpander.Expand(s.Event, Zone(s.Room.TimeZoneId), from, expandTo,
                         MaxOccurrencesPerSeries))
            {
                if (skipped.Contains((s.Event.Id, o.Date))) continue;
                items.Add((s, o, o.StartsAtUtc));
            }

            if (items.Count >= MaxMergedRows) break;
        }

        var ordered = items.OrderBy(i => i.Start).ThenBy(i => i.Row.Event.Id).ToList();
        var skip = (int)Math.Min((long)(filter.Page - 1) * filter.PageSize, int.MaxValue);
        var pageItems = ordered.Skip(skip).Take(filter.PageSize).ToList();

        var pageIds = pageItems.Select(i => i.Row.Event.Id).Distinct().ToArray();
        var pageParticipants = await db.EventParticipants.AsNoTracking()
            .Where(p => pageIds.Contains(p.EventId)).OrderBy(p => p.MemberId).ToListAsync(ct);

        return new PagedResponse<EventResponse>(
            pageItems.Select(i => Map(i.Row, pageParticipants.Where(p => p.EventId == i.Row.Event.Id).Select(p => p.MemberId).ToArray(), i.Occ)).ToArray(),
            ordered.Count, filter.Page, filter.PageSize);
    }

    public async Task<EventResponse> GetAsync(int roomId, int eventId, CancellationToken ct)
    {
        var (room, _) = await access.RequireMemberAsync(roomId, ct);
        access.RequireReadable(room);
        return await ResponseAsync(db, roomId, eventId, ct);
    }

    public Task<EventResponse> CreateAsync(int roomId, CreateEventRequest request, CancellationToken ct)
    {
        return mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var member = await RequireWriterAsync(context, room, ct);
            await ValidateTypeAsync(context, roomId, request.EventTypeId, ct);
            var ev = new CalendarEvent
            {
                RoomId = roomId, AuthorMemberId = member.Id, Title = Title(request.Title),
                Description = EmptyToNull(request.Description), EventTypeId = request.EventTypeId,
                Color = request.Color?.ToUpperInvariant(), IsHighPriority = request.IsHighPriority,
                IsPinned = request.IsPinned
            };
            EventTime.Apply(ev, request.Time, room.TimeZoneId);
            await ApplyRecurrenceAsync(context, ev, request.Recurrence, room, ct);
            var participants = await ValidateParticipantsAsync(context, roomId,
                request.ParticipantMemberIds ?? [member.Id], ct);
            context.CalendarEvents.Add(ev);
            await context.SaveChangesAsync(ct);
            context.EventParticipants.AddRange(participants.Select(id => new EventParticipant
                { EventId = ev.Id, RoomId = roomId, MemberId = id }));
            await context.SaveChangesAsync(ct);
            return await ResponseAsync(context, roomId, ev.Id, ct);
        }, ct);
    }

    public Task<EventResponse> UpdateAsync(int roomId, int eventId, UpdateEventRequest request, CancellationToken ct)
    {
        return mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var member = await RequireWriterAsync(context, room, ct);
            var ev = await FindEventAsync(context, roomId, eventId, ct);
            RequireCanModify(member, room, ev.AuthorMemberId);
            RequireVersion(ev, request.RowVersion);
            RoomAccessService.RequireOwnEntry(member, ev.AuthorMemberId, room.OwnerUserId);
            if (request.Title is not null)
            {
                ev.Title = Title(request.Title);
            }

            if (request.Description is not null)
            {
                ev.Description = EmptyToNull(request.Description);
            }

            if (request.Color is not null)
            {
                ev.Color = EmptyToNull(request.Color)?.ToUpperInvariant();
            }

            if (request.EventTypeId is { } typeId && typeId != ev.EventTypeId)
            {
                await ValidateTypeAsync(context, roomId, typeId, ct);
                ev.EventTypeId = typeId;
            }

            if (request.IsHighPriority is { } priority)
            {
                ev.IsHighPriority = priority;
            }

            if (request.IsPinned is { } pinned)
            {
                ev.IsPinned = pinned;
            }

            if (request.Time is not null)
            {
                EventTime.Apply(ev, request.Time, room.TimeZoneId);
            }
            if (request.Recurrence is not null || request.Time is not null)
            {
                await ApplyRecurrenceAsync(context, ev, request.Recurrence, room, ct);
                await context.EventOccurrenceExceptions.Where(x => x.EventId == eventId).ExecuteDeleteAsync(ct);
            }

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
    }

    public Task<EventResponse> PinAsync(int roomId, int eventId, PinEventRequest request, CancellationToken ct)
    {
        return mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var member = await RequireWriterAsync(context, room, ct);
            var ev = await FindEventAsync(context, roomId, eventId, ct);
            if (ev.AuthorMemberId != member.Id && room.OwnerUserId != actor.UserId)
            {
                throw new ApiException(403, "event_pin_forbidden", "Only the author or room owner may pin this event.");
            }
            RequireCanModify(member, room, ev.AuthorMemberId);
            RequireVersion(ev, request.RowVersion);
            ev.IsPinned = request.IsPinned;
            context.Entry(ev).Property(x => x.UpdatedAtUtc).IsModified = true;
            await context.SaveChangesAsync(ct);
            return await ResponseAsync(context, roomId, eventId, ct);
        }, ct);
    }

    public Task DeleteAsync(int roomId, int eventId, EventVersionRequest request, CancellationToken ct)
    {
        return mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var member = await RequireWriterAsync(context, room, ct);
            var ev = await FindEventAsync(context, roomId, eventId, ct);
            RequireCanModify(member, room, ev.AuthorMemberId);
            RequireVersion(ev, request.RowVersion);
            RoomAccessService.RequireOwnEntry(member, ev.AuthorMemberId, room.OwnerUserId);
            await context.EventParticipants.Where(p => p.EventId == eventId).ExecuteDeleteAsync(ct);
            await context.EventAttachments.Where(a => a.EventId == eventId).ExecuteDeleteAsync(ct);
            context.CalendarEvents.Remove(ev);
            await context.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    private async Task<RoomMember> RequireWriterAsync(AppDbContext context, Room room, CancellationToken ct)
    {
        var roomAccess = new RoomAccessService(context, actor, clock);
        var (_, member) = await roomAccess.RequireMemberAsync(room.Id, ct);
        roomAccess.RequireActive(room);
        return member;
    }
    
    private async Task ApplyRecurrenceAsync(AppDbContext context, CalendarEvent ev, RecurrenceRequest? request,
        Room room, CancellationToken ct)
    {
        if (request is null) return;

        if (request.Frequency == RecurrenceFrequency.None)
        {
            ev.Recurrence = RecurrenceFrequency.None;
            ev.RecurrenceInterval = 1;
            ev.RecurrenceUntil = null;
            return;
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(room.TimeZoneId);
        var first = RecurrenceExpander.FirstDate(ev, tz);
        var errors = new Dictionary<string, string[]>();
        if (request.Interval is < 1 or > 12)
            errors["recurrence"] = ["Repeat interval must be between 1 and 12."];
        if (request.Until is { } until && (until < first || until > first.AddYears(10)))
            errors["recurrence"] = ["Repeat end must be between the first date and ten years later."];
        if (errors.Count != 0) throw ApiException.Validation(errors);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var others = await context.CalendarEvents.CountAsync(e => e.RoomId == room.Id && e.Id != ev.Id
            && e.Recurrence != RecurrenceFrequency.None
            && (e.RecurrenceUntil == null || e.RecurrenceUntil >= today), ct);
        if (others >= MaxSeriesPerRoom)
            throw new ApiException(409, "too_many_series", "This calendar has too many recurring events.");

        ev.Recurrence = request.Frequency;
        ev.RecurrenceInterval = request.Interval;
        ev.RecurrenceUntil = request.Until;
    }
    public Task SkipOccurrenceAsync(int roomId, int eventId, DateOnly date, EventVersionRequest request,
        CancellationToken ct)
    {
        return mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var member = await RequireWriterAsync(context, room, ct);
            var ev = await FindEventAsync(context, roomId, eventId, ct);
            RequireCanModify(member, room, ev.AuthorMemberId);
            RequireVersion(ev, request.RowVersion);

            if (ev.Recurrence == RecurrenceFrequency.None)
                throw new ApiException(409, "event_not_recurring", "This event does not repeat.");

            var tz = TimeZoneInfo.FindSystemTimeZoneById(room.TimeZoneId);
            var from = new DateTimeOffset(date.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            if (!RecurrenceExpander.Expand(ev, tz, from, from.AddDays(3), 10).Any(o => o.Date == date))
                throw ApiException.Validation(new Dictionary<string, string[]>
                    { ["date"] = ["This date is not an occurrence of the event."] });

            if (!await context.EventOccurrenceExceptions.AnyAsync(
                    x => x.EventId == eventId && x.OccurrenceDate == date, ct))
                context.EventOccurrenceExceptions.Add(new EventOccurrenceException
                    { EventId = eventId, OccurrenceDate = date });

            context.Entry(ev).Property(x => x.UpdatedAtUtc).IsModified = true;
            await context.SaveChangesAsync(ct);
            return true;
        }, ct);
    }
    
    private static async Task<CalendarEvent> FindEventAsync(
        AppDbContext context, int roomId, int eventId, CancellationToken ct)
    {
        return await context.CalendarEvents.SingleOrDefaultAsync(e => e.RoomId == roomId && e.Id == eventId, ct)
               ?? throw new ApiException(404, "event_not_found", "Event not found.");
    }

    private static void RequireVersion(CalendarEvent ev, byte[] version)
    {
        if (!ev.RowVersion.AsSpan().SequenceEqual(version))
            throw new ApiException(409, "concurrency_conflict", "Event changed. Reload and retry.");
    }
    private static void RequireCanModify(RoomMember viewer, Room room, int authorMemberId)
    {
        if (viewer.Id == authorMemberId || viewer.UserId == room.OwnerUserId) return;

        throw new ApiException(403, "event_forbidden",
            "Only the author or the room owner may change this event.");
    }

    private static async Task<int[]> ValidateParticipantsAsync(AppDbContext context, int roomId, int[] ids,
        CancellationToken ct)
    {
        var distinct = ids.Distinct().ToArray();
        if (distinct.Any(id => id <= 0) || distinct.Length != ids.Length ||
            await context.RoomMembers.CountAsync(
                m => m.RoomId == roomId && m.LeftAtUtc == null && distinct.Contains(m.Id), ct) != distinct.Length)
        {
            throw ApiException.Validation(new Dictionary<string, string[]>
                { ["participantMemberIds"] = ["Use distinct active members of this room."] });
        }

        return distinct;
    }

    private async Task<EventResponse> ResponseAsync(AppDbContext context, int roomId, int eventId, CancellationToken ct)
    {
        var row = await VisibleEvents(context)
                      .SingleOrDefaultAsync(x => x.Event.RoomId == roomId && x.Event.Id == eventId, ct)
                  ?? throw new ApiException(404, "event_not_found", "Event not found.");
        var participants = await context.EventParticipants.Where(p => p.EventId == eventId)
            .OrderBy(p => p.MemberId).Select(p => p.MemberId).ToArrayAsync(ct);
        return Map(row, participants);
    }

    private EventResponse Map(EventRow row, int[] participants, RecurrenceExpander.Occurrence? occurrence = null)
    {
        var ev = row.Event;
        var now = clock.GetUtcNow();
        var active = row.Room.ArchivedAtUtc is null && row.Room.ExpiresAtUtc > now;
        var own = row.ViewerMemberId == ev.AuthorMemberId || row.Room.OwnerUserId == actor.UserId;
        
        var zone = TimeZoneInfo.FindSystemTimeZoneById(row.Room.TimeZoneId);

        var response = new EventResponse(ev.Id, ev.RoomId, row.Room.Name, row.Room.TimeZoneId,
            row.Room.GetArchivedAtUtc(now) is not null,
            ev.Title, ev.Description, ev.StartsAtUtc, ev.EndsAtUtc, ev.IsAllDay, ev.StartDate, ev.EndDateExclusive,
            ev.TimeZoneId, ev.EventTypeId, row.Type.Name, row.Type.IsArchived,
            (ev.Color ?? row.Type.Color).ToUpperInvariant(), ev.Color,
            ev.IsHighPriority, ev.IsPinned,
            new EventAuthorResponse(row.Author.Id, row.Author.DisplayName, row.Author.GuestSessionId != null,
                row.Author.UserId == row.Room.OwnerUserId, row.Author.LeftAtUtc != null), participants, ev.RowVersion,
            new EventPermissions(active && own, active && own,
                active && (own || row.Room.OwnerUserId == actor.UserId)),
            Recurrence: ev.Recurrence,
            RecurrenceInterval: ev.RecurrenceInterval,
            RecurrenceUntil: ev.RecurrenceUntil,
            OccurrenceDate: RecurrenceExpander.FirstDate(ev, zone));

        if (occurrence is not { } o) return response;
        
        return response with
        {
            StartsAtUtc = o.StartsAtUtc,
            EndsAtUtc = o.EndsAtUtc,
            StartDate = o.StartDate,
            EndDateExclusive = o.EndDateExclusive,
            OccurrenceDate = o.Date,
            SeriesStartsAtUtc = ev.StartsAtUtc,
            SeriesEndsAtUtc = ev.EndsAtUtc
        };
    }

    private static async Task ValidateTypeAsync(AppDbContext context, int roomId, int typeId, CancellationToken ct)
    {
        if (!await context.RoomEventTypes.AnyAsync(t => t.RoomId == roomId && t.Id == typeId && !t.IsArchived, ct))
        {
            throw ApiException.Validation(new Dictionary<string, string[]>
                { ["eventTypeId"] = ["Select an active event type from this room."] });
        }
    }

    private static string Title(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw ApiException.Validation(new Dictionary<string, string[]> { ["title"] = ["Title cannot be blank."] })
            : value.Trim();
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }
}