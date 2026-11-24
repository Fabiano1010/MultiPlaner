using MultiPlanerAPI.Models;
using MultiPlanerSharedModels.Contracts.Events;

namespace MultiPlanerAPI.Modules.Events;

internal static class RecurrenceExpander
{
    internal static DateOnly FirstDate(CalendarEvent ev, TimeZoneInfo tz)
    {
        return ev.IsAllDay && ev.StartDate is { } d ? d : DateOnly.FromDateTime(SafeToLocal(ev.StartsAtUtc, tz));
    }

    internal static bool OccursOn(CalendarEvent ev, TimeZoneInfo tz, DateOnly date)
    {
        if (ev.Recurrence == RecurrenceFrequency.None || ev.RecurrenceInterval <= 0 ||
            (ev.RecurrenceUntil is { } until && date > until))
        {
            return false;
        }

        var allDay = ev.IsAllDay && ev.StartDate is not null && ev.EndDateExclusive is not null;
        var localStart = allDay ? ev.StartDate!.Value.ToDateTime(TimeOnly.MinValue) : SafeToLocal(ev.StartsAtUtc, tz);
        var firstDate = DateOnly.FromDateTime(localStart);
        if (date < firstDate)
        {
            return false;
        }

        var step = (long)Math.Floor((date.DayNumber - firstDate.DayNumber) /
                                    (PeriodDays(ev.Recurrence) * ev.RecurrenceInterval));
        for (;; step++)
        {
            if (!TryShift(localStart, ev.Recurrence, step * ev.RecurrenceInterval, out var shifted))
            {
                return false;
            }

            var occDate = DateOnly.FromDateTime(shifted);
            if (occDate > date)
            {
                return false;
            }

            if (occDate == date)
            {
                return TryBuild(ev, allDay, shifted, date, tz, out _);
            }
        }
    }

    internal static IEnumerable<RecurrenceOccurrence> Expand(
        CalendarEvent ev, TimeZoneInfo tz, DateTimeOffset from, DateTimeOffset to, int max)
    {
        if (ev.Recurrence == RecurrenceFrequency.None || ev.RecurrenceInterval <= 0 || max <= 0 || to <= from)
        {
            yield break;
        }

        var allDay = ev.IsAllDay && ev.StartDate is not null && ev.EndDateExclusive is not null;
        var spanDays = allDay ? ev.EndDateExclusive!.Value.DayNumber - ev.StartDate!.Value.DayNumber : 0;
        var duration = ev.EndsAtUtc - ev.StartsAtUtc;
        if ((allDay && spanDays <= 0) || (!allDay && duration <= TimeSpan.Zero))
        {
            yield break;
        }

        var localStart = allDay ? ev.StartDate!.Value.ToDateTime(TimeOnly.MinValue) : SafeToLocal(ev.StartsAtUtc, tz);
        var fromLocal = SafeToLocal(from, tz);
        var skipSpan = allDay ? spanDays + 1.0 : duration.TotalDays + 1.0;
        var skipDays = fromLocal > localStart ? Math.Max(0.0, (fromLocal - localStart).TotalDays - skipSpan) : 0.0;
        var step = (long)Math.Floor(skipDays / (PeriodDays(ev.Recurrence) * ev.RecurrenceInterval));

        var produced = 0;
        for (;; step++)
        {
            if (!TryShift(localStart, ev.Recurrence, step * ev.RecurrenceInterval, out var shifted))
            {
                yield break;
            }

            var date = DateOnly.FromDateTime(shifted);
            if ((ev.RecurrenceUntil is { } until && date > until) ||
                !TryBuild(ev, allDay, shifted, date, tz, out var occ) ||
                occ.StartsAtUtc >= to)
            {
                yield break;
            }

            if (occ.EndsAtUtc <= from)
            {
                continue;
            }

            if (++produced > max)
            {
                yield break;
            }

            yield return occ;
        }
    }

    private static double PeriodDays(RecurrenceFrequency frequency)
    {
        return frequency switch
        {
            RecurrenceFrequency.Daily => 1.0,
            RecurrenceFrequency.Weekly => 7.0,
            RecurrenceFrequency.Monthly => 31.0,
            _ => 366.0
        };
    }

    private static bool TryBuild(
        CalendarEvent ev, bool allDay, DateTime shifted, DateOnly date, TimeZoneInfo tz, out RecurrenceOccurrence occ)
    {
        occ = default;
        if (allDay)
        {
            var span = ev.EndDateExclusive!.Value.DayNumber - ev.StartDate!.Value.DayNumber;
            if (span <= 0 || date.DayNumber > DateOnly.MaxValue.DayNumber - span)
            {
                return false;
            }

            var endDate = date.AddDays(span);
            if (!TryToUtc(date.ToDateTime(TimeOnly.MinValue), tz, out var startUtc) ||
                !TryToUtc(endDate.ToDateTime(TimeOnly.MinValue), tz, out var endUtc))
            {
                return false;
            }

            occ = new RecurrenceOccurrence(startUtc, endUtc, date, date, endDate);
            return true;
        }

        var duration = ev.EndsAtUtc - ev.StartsAtUtc;
        if (duration <= TimeSpan.Zero || !TryToUtc(shifted, tz, out var start) ||
            DateTimeOffset.MaxValue - start < duration)
        {
            return false;
        }

        occ = new RecurrenceOccurrence(start, start + duration, date, null, null);
        return true;
    }

    private static DateTime SafeToLocal(DateTimeOffset utc, TimeZoneInfo tz)
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
        result = default;
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(unspecified))
        {
            var before = unspecified > DateTime.MinValue.AddHours(24) ? unspecified.AddHours(-24) : DateTime.MinValue;
            var after = unspecified < DateTime.MaxValue.AddHours(-24) ? unspecified.AddHours(24) : DateTime.MaxValue;
            var delta = tz.GetUtcOffset(after) - tz.GetUtcOffset(before);
            var gap = delta > TimeSpan.Zero ? delta : TimeSpan.FromHours(1);
            if (DateTime.MaxValue - unspecified < gap)
            {
                return false;
            }

            unspecified = unspecified.Add(gap);
            if (tz.IsInvalidTime(unspecified))
            {
                if (DateTime.MaxValue - unspecified < TimeSpan.FromHours(1))
                {
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
            return false;
        }
    }
}