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
            : DateOnly.FromDateTime(SafeConvertToLocal(ev.StartsAtUtc, tz));

    internal static bool OccursOn(CalendarEvent ev, TimeZoneInfo tz, DateOnly date)
    {
        if (ev.Recurrence == RecurrenceFrequency.None || ev.RecurrenceInterval <= 0)
        {
            return false;
        }

        if (ev.RecurrenceUntil is { } until && date > until)
        {
            return false;
        }

        var allDay = ev.IsAllDay && ev.StartDate is not null && ev.EndDateExclusive is not null;
        var localStart = allDay
            ? ev.StartDate!.Value.ToDateTime(TimeOnly.MinValue)
            : SafeConvertToLocal(ev.StartsAtUtc, tz);

        var firstDate = DateOnly.FromDateTime(localStart);
        if (date < firstDate)
        {
            return false;
        }

        var period = ev.Recurrence switch
        {
            RecurrenceFrequency.Daily => 1.0,
            RecurrenceFrequency.Weekly => 7.0,
            RecurrenceFrequency.Monthly => 31.0,
            _ => 366.0
        };
        var k = (long)Math.Floor((date.DayNumber - firstDate.DayNumber) / (period * ev.RecurrenceInterval));
        for (; ; k++)
        {
            if (!TryShift(localStart, ev.Recurrence, k * ev.RecurrenceInterval, out var shifted))
            {
                return false;
            }

            var occDate = DateOnly.FromDateTime(shifted);
            if (occDate > date)
            {
                return false;
            }

            if (occDate < date)
            {
                continue;
            }

            if (allDay)
            {
                var spanDays = ev.EndDateExclusive!.Value.DayNumber - ev.StartDate!.Value.DayNumber;
                if (spanDays <= 0 || date.DayNumber > DateOnly.MaxValue.DayNumber - spanDays)
                {
                    return false;
                }

                return TryToUtc(date.ToDateTime(TimeOnly.MinValue), tz, out _) &&
                       TryToUtc(date.AddDays(spanDays).ToDateTime(TimeOnly.MinValue), tz, out _);
            }

            var duration = ev.EndsAtUtc - ev.StartsAtUtc;
            return duration > TimeSpan.Zero &&
                   TryToUtc(shifted, tz, out var startUtc) &&
                   DateTimeOffset.MaxValue - startUtc >= duration;
        }
    }

    internal static IEnumerable<Occurrence> Expand(
        CalendarEvent ev, TimeZoneInfo tz, DateTimeOffset from, DateTimeOffset to, int max)
    {
        if (ev.Recurrence == RecurrenceFrequency.None || ev.RecurrenceInterval <= 0 || max <= 0 || to <= from)
        {
            yield break;
        }

        var allDay = ev.IsAllDay && ev.StartDate is not null && ev.EndDateExclusive is not null;
        DateTime localStart;
        TimeSpan timedDuration = default;
        var allDaySpanDays = 0;
        double skipSpanDays;

        if (allDay)
        {
            allDaySpanDays = ev.EndDateExclusive!.Value.DayNumber - ev.StartDate!.Value.DayNumber;
            if (allDaySpanDays <= 0) yield break;
            localStart = ev.StartDate.Value.ToDateTime(TimeOnly.MinValue);
            skipSpanDays = allDaySpanDays + 1.0;
        }
        else
        {
            timedDuration = ev.EndsAtUtc - ev.StartsAtUtc;
            if (timedDuration <= TimeSpan.Zero) yield break;
            localStart = SafeConvertToLocal(ev.StartsAtUtc, tz);
            skipSpanDays = timedDuration.TotalDays + 1.0;
        }

        var fromLocal = SafeConvertToLocal(from, tz);
        var period = ev.Recurrence switch
        {
            RecurrenceFrequency.Daily => 1.0,
            RecurrenceFrequency.Weekly => 7.0,
            RecurrenceFrequency.Monthly => 31.0,
            _ => 366.0
        };
        var skipDays = fromLocal > localStart
            ? Math.Max(0.0, (fromLocal - localStart).TotalDays - skipSpanDays)
            : 0.0;
        var k = (long)Math.Floor(skipDays / (period * ev.RecurrenceInterval));

        var produced = 0;
        for (; ; k++)
        {
            if (!TryShift(localStart, ev.Recurrence, k * ev.RecurrenceInterval, out var s)) yield break;

            var date = DateOnly.FromDateTime(s);
            if (ev.RecurrenceUntil is { } until && date > until) yield break;

            if (allDay)
            {
                if (date.DayNumber > DateOnly.MaxValue.DayNumber - allDaySpanDays) yield break;
                var endDate = date.AddDays(allDaySpanDays);
                if (!TryToUtc(date.ToDateTime(TimeOnly.MinValue), tz, out var startUtc)) yield break;
                if (startUtc >= to) yield break;
                if (!TryToUtc(endDate.ToDateTime(TimeOnly.MinValue), tz, out var endUtc)) yield break;
                if (endUtc <= from) continue;
                if (++produced > max) yield break;

                yield return new Occurrence(startUtc, endUtc, date, date, endDate);
            }
            else
            {
                if (!TryToUtc(s, tz, out var startUtc)) yield break;
                if (startUtc >= to) yield break;
                if (DateTimeOffset.MaxValue - startUtc < timedDuration) yield break;

                var endUtc = startUtc + timedDuration;
                if (endUtc <= from) continue;
                if (++produced > max) yield break;

                yield return new Occurrence(startUtc, endUtc, date, null, null);
            }
        }
    }

    private static DateTime SafeConvertToLocal(DateTimeOffset utc, TimeZoneInfo tz)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(utc, tz).DateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return utc.UtcDateTime;
        }
    }

    private static bool TryShift(DateTime d, RecurrenceFrequency f, long n, out DateTime result)
    {
        try
        {
            result = f switch
            {
                RecurrenceFrequency.Daily => d.AddDays(n),
                RecurrenceFrequency.Weekly => d.AddDays(checked(7 * n)),
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

    private static bool TryToUtc(DateTime local, TimeZoneInfo tz, out DateTimeOffset result)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(unspecified))
        {
            var gap = GetSpringGap(unspecified, tz);
            if (DateTime.MaxValue - unspecified < gap)
            {
                result = default;
                return false;
            }

            unspecified = unspecified.Add(gap);
            if (tz.IsInvalidTime(unspecified))
            {
                if (DateTime.MaxValue - unspecified < TimeSpan.FromHours(1))
                {
                    result = default;
                    return false;
                }

                unspecified = unspecified.AddHours(1);
            }
        }

        // Consistent with EventTime.MidnightUtc: when a local time repeats (fall-back), pick the earlier occurrence.
        var offset = tz.IsAmbiguousTime(unspecified)
            ? tz.GetAmbiguousTimeOffsets(unspecified).Max()
            : tz.GetUtcOffset(unspecified);
        try
        {
            result = new DateTimeOffset(unspecified, offset).ToUniversalTime();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            result = default;
            return false;
        }
    }

    private static TimeSpan GetSpringGap(DateTime invalidLocal, TimeZoneInfo tz)
    {
        var before = invalidLocal > DateTime.MinValue.AddHours(24) ? invalidLocal.AddHours(-24) : DateTime.MinValue;
        var after = invalidLocal < DateTime.MaxValue.AddHours(-24) ? invalidLocal.AddHours(24) : DateTime.MaxValue;
        var delta = tz.GetUtcOffset(after) - tz.GetUtcOffset(before);
        return delta > TimeSpan.Zero ? delta : TimeSpan.FromHours(1);
    }
}
