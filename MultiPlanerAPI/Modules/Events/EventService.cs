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
    private const int MaxSeriesPerRoom = 50;
    private const int MaxOccurrencesPerSeries = 400;
    private const int MaxWindowDays = 400;
    private const int MaxSeriesPerQuery = 500;
    private const int MaxMergedRows = 5000;

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

    public async Task<PagedResponse<EventResponse>> ListAsync(int? roomId, EventListRequest filter,
        CancellationToken ct)
    {
        if (roomId is { } id)
        {
            if (filter.RoomId is not null && id != filter.RoomId)
            {
                throw ApiException.Validation("roomId", "Room filter must match the route.");
            }

            var (room, _) = await access.RequireMemberAsync(id, ct);
            access.RequireReadable(room);
        }

        var query = ApplyFilters(VisibleEvents(db), roomId, filter);
        var from = filter.From!.Value;
        var to = filter.To!.Value;
        var singlesQuery = query.Where(x =>
            x.Event.Recurrence == RecurrenceFrequency.None && x.Event.StartsAtUtc < to && x.Event.EndsAtUtc > from);

        var expandLimit = ClampAddDays(from, MaxWindowDays);
        var expandTo = to < expandLimit ? to : expandLimit;
        var fromDay = DateOnly.FromDateTime(ClampAddDays(from, -1).UtcDateTime);

        var series = await query
            .Where(x => x.Event.Recurrence != RecurrenceFrequency.None && x.Event.StartsAtUtc < expandTo &&
                        (x.Event.RecurrenceUntil == null || x.Event.RecurrenceUntil >= fromDay))
            .OrderBy(x => x.Event.Id).Take(MaxSeriesPerQuery).ToListAsync(ct);

        var zones = new Dictionary<string, TimeZoneInfo>(StringComparer.Ordinal);

        TimeZoneInfo Zone(string id)
        {
            return zones.TryGetValue(id, out var z) ? z : zones[id] = TimeZoneInfo.FindSystemTimeZoneById(id);
        }

        var offset = (long)(filter.Page - 1) * filter.PageSize;
        if (series.Count == 0)
        {
            var count = await singlesQuery.CountAsync(ct);
            if (offset > int.MaxValue || offset >= count)
            {
                return new PagedResponse<EventResponse>([], count, filter.Page, filter.PageSize);
            }

            var rows = await singlesQuery.OrderBy(x => x.Event.StartsAtUtc).ThenBy(x => x.Event.Id)
                .Skip((int)offset).Take(filter.PageSize).ToListAsync(ct);
            var map = await LoadParticipantsMapAsync(db, rows.Select(x => x.Event.Id).ToArray(), ct);
            return new PagedResponse<EventResponse>(
                rows.Select(x => Map(x, map.GetValueOrDefault(x.Event.Id, []),
                    cachedZone: Zone(x.Event.TimeZoneId ?? x.Room.TimeZoneId))).ToArray(),
                count, filter.Page, filter.PageSize);
        }

        if (offset >= MaxMergedRows)
        {
            return new PagedResponse<EventResponse>([], 0, filter.Page, filter.PageSize);
        }

        var singles = await singlesQuery.OrderBy(x => x.Event.StartsAtUtc).ThenBy(x => x.Event.Id)
            .Take(MaxMergedRows).ToListAsync(ct);
        var toDay = DateOnly.FromDateTime(ClampAddDays(expandTo, 1).UtcDateTime);
        var seriesIds = series.Select(x => x.Event.Id).ToArray();
        var skipped = (await db.EventOccurrenceExceptions.AsNoTracking()
                .Where(x => seriesIds.Contains(x.EventId) && x.OccurrenceDate >= fromDay && x.OccurrenceDate <= toDay)
                .Select(x => new { x.EventId, x.OccurrenceDate }).ToListAsync(ct))
            .Select(x => (x.EventId, x.OccurrenceDate)).ToHashSet();

        var (pageItems, totalCount) = MergeEvents(singles, series, skipped, Zone, from, expandTo, (int)offset,
            filter.PageSize, ct);
        var participants =
            await LoadParticipantsMapAsync(db, pageItems.Select(i => i.Row.Event.Id).Distinct().ToArray(), ct);
        return new PagedResponse<EventResponse>(
            pageItems.Select(i => Map(i.Row, participants.GetValueOrDefault(i.Row.Event.Id, []), i.Occ,
                Zone(i.Row.Event.TimeZoneId ?? i.Row.Room.TimeZoneId))).ToArray(),
            totalCount, filter.Page, filter.PageSize);
    }

    private IQueryable<EventRow> ApplyFilters(IQueryable<EventRow> query, int? roomId, EventListRequest filter)
    {
        if ((roomId ?? filter.RoomId) is { } requestedRoom)
        {
            query = query.Where(x => x.Event.RoomId == requestedRoom);
        }

        if (roomId is null)
        {
            var now = clock.GetUtcNow();
            query = filter.Status switch
            {
                "active" => query.Where(x => x.Room.ArchivedAtUtc == null && x.Room.ExpiresAtUtc > now),
                "archived" => query.Where(x =>
                    x.Room.ArchivedAtUtc != null || (x.Room.ArchiveOnExpiry && x.Room.ExpiresAtUtc <= now)),
                _ => query
            };
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

        return query;
    }

    private static (List<(EventRow Row, RecurrenceOccurrence? Occ)> PageItems, int TotalCount) MergeEvents(
        List<EventRow> singles, List<EventRow> series, HashSet<(int EventId, DateOnly Date)> skipped,
        Func<string, TimeZoneInfo> zone, DateTimeOffset from, DateTimeOffset expandTo, int skip, int pageSize,
        CancellationToken ct)
    {
        var queue = new PriorityQueue<(int Source, EventRow Row, RecurrenceOccurrence? Occ), (DateTimeOffset, int)>();
        var enums = new IEnumerator<RecurrenceOccurrence>[series.Count];
        var singleIdx = 0;

        bool Next(IEnumerator<RecurrenceOccurrence> e, int eventId, out RecurrenceOccurrence occ)
        {
            while (e.MoveNext())
            {
                if (!skipped.Contains((eventId, e.Current.Date)))
                {
                    occ = e.Current;
                    return true;
                }
            }

            occ = default;
            return false;
        }

        try
        {
            if (singles.Count > 0)
            {
                queue.Enqueue((-1, singles[0], null), (singles[0].Event.StartsAtUtc, singles[0].Event.Id));
            }

            for (var i = 0; i < series.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var s = series[i];
                enums[i] = RecurrenceExpander.Expand(s.Event, zone(s.Event.TimeZoneId ?? s.Room.TimeZoneId),
                    from, expandTo, MaxOccurrencesPerSeries).GetEnumerator();
                if (Next(enums[i], s.Event.Id, out var occ))
                {
                    queue.Enqueue((i, s, occ), (occ.StartsAtUtc, s.Event.Id));
                }
            }

            var page = new List<(EventRow Row, RecurrenceOccurrence? Occ)>(pageSize);
            var total = 0;
            while (queue.TryDequeue(out var entry, out _))
            {
                ct.ThrowIfCancellationRequested();
                if (total >= skip && page.Count < pageSize)
                {
                    page.Add((entry.Row, entry.Occ));
                }

                if (++total >= MaxMergedRows)
                {
                    break;
                }

                if (entry.Source < 0)
                {
                    if (++singleIdx < singles.Count)
                    {
                        var next = singles[singleIdx];
                        queue.Enqueue((-1, next, null), (next.Event.StartsAtUtc, next.Event.Id));
                    }
                }
                else if (Next(enums[entry.Source], entry.Row.Event.Id, out var nextOcc))
                {
                    queue.Enqueue((entry.Source, entry.Row, nextOcc), (nextOcc.StartsAtUtc, entry.Row.Event.Id));
                }
            }

            return (page, total);
        }
        finally
        {
            foreach (var e in enums)
            {
                e?.Dispose();
            }
        }
    }

    public async Task<EventResponse> GetAsync(int roomId, int eventId, CancellationToken ct)
    {
        var (room, _) = await access.RequireMemberAsync(roomId, ct);
        access.RequireReadable(room);
        var row = await VisibleEvents(db)
                      .SingleOrDefaultAsync(x => x.Event.RoomId == roomId && x.Event.Id == eventId, ct)
                  ?? throw new ApiException(404, "event_not_found", "Event not found.");
        var participants = await db.EventParticipants.Where(p => p.EventId == eventId)
            .OrderBy(p => p.MemberId).Select(p => p.MemberId).ToArrayAsync(ct);
        return Map(row, participants);
    }

    public async Task<EventResponse> CreateAsync(int roomId, CreateEventRequest request, CancellationToken ct)
    {
        var (row, participants) = await mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var member = await RequireWriterAsync(context, room, ct);
            var type = await RequireActiveTypeAsync(context, roomId, request.EventTypeId, ct);
            var ev = new CalendarEvent
            {
                RoomId = roomId, AuthorMemberId = member.Id, Title = Title(request.Title),
                Description = EmptyToNull(request.Description), EventTypeId = request.EventTypeId,
                Color = request.Color?.ToUpperInvariant(), IsHighPriority = request.IsHighPriority,
                IsPinned = request.IsPinned
            };
            EventTime.Apply(ev, request.Time, room.TimeZoneId);
            await ApplyRecurrenceAsync(context, ev, request.Recurrence, room, ct);
            var validated =
                await ValidateParticipantsAsync(context, roomId, request.ParticipantMemberIds ?? [member.Id], ct);
            context.CalendarEvents.Add(ev);
            await context.SaveChangesAsync(ct);
            context.EventParticipants.AddRange(validated.Select(id => new EventParticipant
                { EventId = ev.Id, RoomId = roomId, MemberId = id }));
            await context.SaveChangesAsync(ct);
            return (new EventRow { Event = ev, Type = type, Room = room, Author = member, ViewerMemberId = member.Id },
                validated);
        }, ct);
        return Map(row, participants);
    }

    public async Task<EventResponse> UpdateAsync(int roomId, int eventId, UpdateEventRequest request,
        CancellationToken ct)
    {
        var (row, participants) = await mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var member = await RequireWriterAsync(context, room, ct);
            var ev = await FindEventAsync(context, roomId, eventId, ct);
            RequireCanModify(member, room, ev.AuthorMemberId);
            RequireVersion(ev, request.RowVersion);

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

            if (request.IsHighPriority is { } priority)
            {
                ev.IsHighPriority = priority;
            }

            if (request.IsPinned is { } pinned)
            {
                ev.IsPinned = pinned;
            }

            RoomEventType? type = null;
            if (request.EventTypeId is { } typeId && typeId != ev.EventTypeId)
            {
                type = await RequireActiveTypeAsync(context, roomId, typeId, ct);
                ev.EventTypeId = typeId;
            }

            var oldSchedule = (ev.StartsAtUtc, ev.EndsAtUtc, ev.Recurrence, ev.RecurrenceInterval, ev.RecurrenceUntil);
            if (request.Time is not null)
            {
                EventTime.Apply(ev, request.Time, room.TimeZoneId);
            }

            if (request.Recurrence is not null || request.Time is not null)
            {
                await ApplyRecurrenceAsync(context, ev, request.Recurrence, room, ct);
            }

            if ((ev.StartsAtUtc, ev.EndsAtUtc, ev.Recurrence, ev.RecurrenceInterval, ev.RecurrenceUntil) != oldSchedule)
            {
                await context.EventOccurrenceExceptions.Where(x => x.EventId == eventId).ExecuteDeleteAsync(ct);
            }

            int[]? updatedParticipants = null;
            if (request.ParticipantMemberIds is { } ids)
            {
                updatedParticipants = await ValidateParticipantsAsync(context, roomId, ids, ct);
                await context.EventParticipants.Where(p => p.EventId == eventId).ExecuteDeleteAsync(ct);
                context.EventParticipants.AddRange(updatedParticipants.Select(id =>
                    new EventParticipant { EventId = eventId, RoomId = roomId, MemberId = id }));
            }

            // Participant-only and no-op updates still advance the concurrency token.
            context.Entry(ev).Property(x => x.UpdatedAtUtc).IsModified = true;
            await context.SaveChangesAsync(ct);
            return await LoadRowForResponseAsync(context, room, member, ev, type, updatedParticipants, ct);
        }, ct);
        return Map(row, participants);
    }

    public async Task<EventResponse> PinAsync(int roomId, int eventId, PinEventRequest request, CancellationToken ct)
    {
        var (row, participants) = await mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var member = await RequireWriterAsync(context, room, ct);
            var ev = await FindEventAsync(context, roomId, eventId, ct);
            if (ev.AuthorMemberId != member.Id && room.OwnerUserId != actor.UserId)
            {
                throw new ApiException(403, "event_pin_forbidden", "Only the author or room owner may pin this event.");
            }

            RequireVersion(ev, request.RowVersion);
            ev.IsPinned = request.IsPinned;
            context.Entry(ev).Property(x => x.UpdatedAtUtc).IsModified = true;
            await context.SaveChangesAsync(ct);
            return await LoadRowForResponseAsync(context, room, member, ev, null, null, ct);
        }, ct);
        return Map(row, participants);
    }

    public Task DeleteAsync(int roomId, int eventId, EventVersionRequest request, CancellationToken ct)
    {
        return mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var member = await RequireWriterAsync(context, room, ct);
            var ev = await FindEventAsync(context, roomId, eventId, ct);
            RequireCanModify(member, room, ev.AuthorMemberId);
            RequireVersion(ev, request.RowVersion);
            await context.EventParticipants.Where(p => p.EventId == eventId).ExecuteDeleteAsync(ct);
            await context.EventAttachments.Where(a => a.EventId == eventId).ExecuteDeleteAsync(ct);
            context.CalendarEvents.Remove(ev);
            await context.SaveChangesAsync(ct);
            return true;
        }, ct);
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
            {
                throw new ApiException(409, "event_not_recurring", "This event does not repeat.");
            }

            var tz = TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId ?? room.TimeZoneId);
            if (!RecurrenceExpander.OccursOn(ev, tz, date))
            {
                throw ApiException.Validation("date", "This date is not an occurrence of the event.");
            }

            if (!await context.EventOccurrenceExceptions.AnyAsync(x => x.EventId == eventId && x.OccurrenceDate == date,
                    ct))
            {
                context.EventOccurrenceExceptions.Add(new EventOccurrenceException
                    { EventId = eventId, OccurrenceDate = date });
            }

            context.Entry(ev).Property(x => x.UpdatedAtUtc).IsModified = true;
            await context.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    private async Task ApplyRecurrenceAsync(
        AppDbContext context, CalendarEvent ev, RecurrenceRequest? request, Room room, CancellationToken ct)
    {
        if (request is { Frequency: RecurrenceFrequency.None })
        {
            ev.Recurrence = RecurrenceFrequency.None;
            ev.RecurrenceInterval = 1;
            ev.RecurrenceUntil = null;
            return;
        }

        if (request is null && ev.Recurrence == RecurrenceFrequency.None)
        {
            return;
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId ?? room.TimeZoneId);
        var first = RecurrenceExpander.FirstDate(ev, tz);
        var maxUntil = first.Year <= DateOnly.MaxValue.Year - 10 ? first.AddYears(10) : DateOnly.MaxValue;
        var until = request is null ? ev.RecurrenceUntil : request.Until;

        var errors = new Dictionary<string, string[]>();
        if (request is not null && request.Interval is < 1 or > 12)
        {
            errors["recurrence"] = ["Repeat interval must be between 1 and 12."];
        }

        if (until is { } u && (u < first || u > maxUntil))
        {
            errors["recurrence"] = ["Repeat end must be between the first date and ten years later."];
        }

        if (errors.Count != 0)
        {
            throw ApiException.Validation(errors);
        }

        if (request is null)
        {
            return;
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var others = await context.CalendarEvents.CountAsync(e =>
            e.RoomId == room.Id && e.Id != ev.Id && e.Recurrence != RecurrenceFrequency.None &&
            (e.RecurrenceUntil == null || e.RecurrenceUntil >= today), ct);
        if (others >= MaxSeriesPerRoom)
        {
            throw new ApiException(409, "too_many_series", "This calendar has too many recurring events.");
        }

        ev.Recurrence = request.Frequency;
        ev.RecurrenceInterval = request.Interval;
        ev.RecurrenceUntil = request.Until;
    }

    private static async Task<Dictionary<int, int[]>> LoadParticipantsMapAsync(
        AppDbContext context, int[] eventIds, CancellationToken ct)
    {
        if (eventIds.Length == 0)
        {
            return [];
        }

        var rows = await context.EventParticipants.AsNoTracking()
            .Where(p => eventIds.Contains(p.EventId))
            .OrderBy(p => p.EventId).ThenBy(p => p.MemberId)
            .Select(p => new { p.EventId, p.MemberId }).ToListAsync(ct);
        return rows.GroupBy(r => r.EventId).ToDictionary(g => g.Key, g => g.Select(r => r.MemberId).ToArray());
    }

    private static async Task<(EventRow Row, int[] Participants)> LoadRowForResponseAsync(
        AppDbContext context, Room room, RoomMember member, CalendarEvent ev,
        RoomEventType? loadedType, int[]? loadedParticipants, CancellationToken ct)
    {
        var type = loadedType ??
                   await context.RoomEventTypes.AsNoTracking().SingleAsync(t => t.Id == ev.EventTypeId, ct);
        var author = ev.AuthorMemberId == member.Id
            ? member
            : await context.RoomMembers.AsNoTracking().SingleAsync(m => m.Id == ev.AuthorMemberId, ct);
        var participants = loadedParticipants ?? await context.EventParticipants.AsNoTracking()
            .Where(p => p.EventId == ev.Id).OrderBy(p => p.MemberId).Select(p => p.MemberId).ToArrayAsync(ct);
        return (new EventRow { Event = ev, Type = type, Room = room, Author = author, ViewerMemberId = member.Id },
            participants);
    }

    private async Task<RoomMember> RequireWriterAsync(AppDbContext context, Room room, CancellationToken ct)
    {
        var roomAccess = access.For(context);
        var member = await roomAccess.RequireMemberAsync(room, ct);
        roomAccess.RequireActive(room);
        return member;
    }

    private static DateTimeOffset ClampAddDays(DateTimeOffset value, int days)
    {
        return days >= 0 && value > DateTimeOffset.MaxValue.AddDays(-days) ? DateTimeOffset.MaxValue :
            days < 0 && value < DateTimeOffset.MinValue.AddDays(-days) ? DateTimeOffset.MinValue :
            value.AddDays(days);
    }

    private static async Task<CalendarEvent> FindEventAsync(AppDbContext context, int roomId, int eventId,
        CancellationToken ct)
    {
        return await context.CalendarEvents.SingleOrDefaultAsync(e => e.RoomId == roomId && e.Id == eventId, ct)
               ?? throw new ApiException(404, "event_not_found", "Event not found.");
    }

    private static void RequireVersion(CalendarEvent ev, byte[] version)
    {
        ApiException.RequireVersion(ev.RowVersion, version, "Event changed. Reload and retry.");
    }

    private static void RequireCanModify(RoomMember viewer, Room room, int authorMemberId)
    {
        if (viewer.Id != authorMemberId && viewer.UserId != room.OwnerUserId)
        {
            throw new ApiException(403, "event_forbidden", "Only the author or the room owner may change this event.");
        }
    }

    private static async Task<int[]> ValidateParticipantsAsync(AppDbContext context, int roomId, int[] ids,
        CancellationToken ct)
    {
        var distinct = ids.Distinct().ToArray();
        if (distinct.Any(id => id <= 0) || distinct.Length != ids.Length ||
            await context.RoomMembers.CountAsync(
                m => m.RoomId == roomId && m.LeftAtUtc == null && distinct.Contains(m.Id), ct) != distinct.Length)
        {
            throw ApiException.Validation("participantMemberIds", "Use distinct active members of this room.");
        }

        return distinct;
    }

    private EventResponse Map(EventRow row, int[] participants, RecurrenceOccurrence? occurrence = null,
        TimeZoneInfo? cachedZone = null)
    {
        var ev = row.Event;
        var now = clock.GetUtcNow();
        var active = row.Room.ArchivedAtUtc is null && row.Room.ExpiresAtUtc > now;
        var own = row.ViewerMemberId == ev.AuthorMemberId || row.Room.OwnerUserId == actor.UserId;
        var occurrenceDate = occurrence is { } occ
            ? occ.Date
            : RecurrenceExpander.FirstDate(ev,
                cachedZone ?? TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId ?? row.Room.TimeZoneId));

        var response = new EventResponse(
            ev.Id, ev.RoomId, row.Room.Name, row.Room.TimeZoneId, row.Room.GetArchivedAtUtc(now) is not null,
            ev.Title, ev.Description, ev.StartsAtUtc, ev.EndsAtUtc, ev.IsAllDay, ev.StartDate, ev.EndDateExclusive,
            ev.TimeZoneId, ev.EventTypeId, row.Type.Name, row.Type.IsArchived,
            (ev.Color ?? row.Type.Color).ToUpperInvariant(), ev.Color, ev.IsHighPriority, ev.IsPinned,
            new EventAuthorResponse(row.Author.Id, row.Author.DisplayName, row.Author.GuestSessionId != null,
                row.Author.UserId == row.Room.OwnerUserId, row.Author.LeftAtUtc != null),
            participants, ev.RowVersion,
            new EventPermissions(active && own, active && own, active && own),
            ev.Recurrence, ev.RecurrenceInterval, ev.RecurrenceUntil, occurrenceDate);

        return occurrence is { } o
            ? response with
            {
                StartsAtUtc = o.StartsAtUtc, EndsAtUtc = o.EndsAtUtc, StartDate = o.StartDate,
                EndDateExclusive = o.EndDateExclusive, OccurrenceDate = o.Date,
                SeriesStartsAtUtc = ev.StartsAtUtc, SeriesEndsAtUtc = ev.EndsAtUtc
            }
            : response;
    }

    private static async Task<RoomEventType> RequireActiveTypeAsync(AppDbContext context, int roomId, int typeId,
        CancellationToken ct)
    {
        return await context.RoomEventTypes.AsNoTracking()
                   .SingleOrDefaultAsync(t => t.RoomId == roomId && t.Id == typeId && !t.IsArchived, ct)
               ?? throw ApiException.Validation("eventTypeId", "Select an active event type from this room.");
    }

    private static string Title(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw ApiException.Validation("title", "Title cannot be blank.")
            : value.Trim();
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }
}