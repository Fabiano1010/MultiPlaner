using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerSharedModels.Contracts.Events;

namespace MultiPlanerAPI.Modules.Events;

public static class EventTime
{
    public static void Apply(CalendarEvent target, EventTimeRequest value, string roomTimeZone)
    {
        if (!value.IsAllDay)
        {
            if (value.StartsAtUtc is not { } start || value.EndsAtUtc is not { } end || end <= start ||
                value.StartDate is not null || value.EndDateExclusive is not null)
                throw Invalid("Timed events require an increasing pair of timestamps and no local dates.");
            target.StartsAtUtc = start.ToUniversalTime();
            target.EndsAtUtc = end.ToUniversalTime();
            target.StartDate = target.EndDateExclusive = null;
            target.TimeZoneId = null;
        }
        else
        {
            if (value.StartDate is not { } start || value.EndDateExclusive is not { } end || end <= start ||
                value.StartsAtUtc is not null || value.EndsAtUtc is not null)
                throw Invalid("All-day events require increasing local dates with an exclusive end and no timestamps.");
            // Keep the original all-day zone when a room's display zone changes.
            var zoneId = target.IsAllDay && target.TimeZoneId is not null ? target.TimeZoneId : roomTimeZone;
            var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            target.StartsAtUtc = MidnightUtc(start, zone);
            target.EndsAtUtc = MidnightUtc(end, zone);
            if (target.EndsAtUtc <= target.StartsAtUtc) throw Invalid("Event must have a positive duration.");
            target.StartDate = start;
            target.EndDateExclusive = end;
            target.TimeZoneId = zoneId;
        }
        target.IsAllDay = value.IsAllDay;
    }

    private static DateTimeOffset MidnightUtc(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) throw Invalid("A selected local midnight does not exist in this time zone.");
        // When midnight repeats, the first occurrence is the start of that calendar day.
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        try { return new DateTimeOffset(local, offset).ToUniversalTime(); }
        catch (ArgumentException) { throw Invalid("Date is outside the supported UTC range."); }
    }

    private static ApiException Invalid(string message) =>
        ApiException.Validation(new Dictionary<string, string[]> { ["time"] = [message] });
}
