using MultiPlanerAPI.Models;
using MultiPlanerSharedModels.Contracts.Events;

namespace MultiPlanerAPI.Modules.Events;


internal static class RecurrenceExpander
{
    internal readonly record struct Occurrence(
        DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc, DateOnly Date,
        DateOnly? StartDate, DateOnly? EndDateExclusive);

    internal static DateOnly FirstDate(CalendarEvent ev, TimeZoneInfo tz) =>
        ev.IsAllDay && ev.StartDate is { } d
            ? d
            : DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ev.StartsAtUtc, tz).DateTime);

    internal static IEnumerable<Occurrence> Expand(
        CalendarEvent ev, TimeZoneInfo tz, DateTimeOffset from, DateTimeOffset to, int max)
    {
        DateTime localStart;
        TimeSpan span;
        var allDay = ev.IsAllDay && ev.StartDate is not null && ev.EndDateExclusive is not null;
        if (allDay)
        {
            localStart = ev.StartDate!.Value.ToDateTime(TimeOnly.MinValue);
            span = TimeSpan.FromDays(ev.EndDateExclusive!.Value.DayNumber - ev.StartDate.Value.DayNumber);
        }
        else
        {
            localStart = TimeZoneInfo.ConvertTime(ev.StartsAtUtc, tz).DateTime;
            span = TimeZoneInfo.ConvertTime(ev.EndsAtUtc, tz).DateTime - localStart;
        }
        
        var fromLocal = TimeZoneInfo.ConvertTime(from, tz).DateTime;
        var period = ev.Recurrence switch { RecurrenceFrequency.Daily => 1.0, RecurrenceFrequency.Weekly => 7.0,
            RecurrenceFrequency.Monthly => 31.0, _ => 366.0 };
        var skipDays = Math.Max(0, (fromLocal - localStart - span).TotalDays);
        var k = (long)Math.Floor(skipDays / (period * ev.RecurrenceInterval));

        var produced = 0;
        for (; ; k++)
        {
            if (!TryShift(localStart, ev.Recurrence, k * ev.RecurrenceInterval, out var s)) yield break;

            var date = DateOnly.FromDateTime(s);
            if (ev.RecurrenceUntil is { } until && date > until) yield break;

            var startUtc = ToUtc(s, tz);
            if (startUtc >= to) yield break;

            var endUtc = ToUtc(s + span, tz);
            if (endUtc <= from) continue;
            if (++produced > max) yield break;

            yield return allDay
                ? new Occurrence(startUtc, endUtc, date, date, date.AddDays((int)span.TotalDays))
                : new Occurrence(startUtc, endUtc, date, null, null);
        }
    }

    private static bool TryShift(DateTime d, RecurrenceFrequency f, long n, out DateTime result)
    {
        try
        {
            result = f switch
            {
                RecurrenceFrequency.Daily => d.AddDays(n),
                RecurrenceFrequency.Weekly => d.AddDays(7 * n),
                RecurrenceFrequency.Monthly => d.AddMonths(checked((int)n)),
                _ => d.AddYears(checked((int)n))
            };
            return true;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        {
            result = default;
            return false;
        }
    }

    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo tz)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);   // time change
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, tz), TimeSpan.Zero);
    }
}