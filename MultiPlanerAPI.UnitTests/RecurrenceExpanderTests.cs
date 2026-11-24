using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Events;
using MultiPlanerSharedModels.Contracts.Events;

namespace MultiPlanerAPI.UnitTests;

public sealed class RecurrenceExpanderTests
{
    [Fact]
    public void TimedEventCrossingFallBackDstPreservesPositiveUtcDuration()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        // 2026-10-25 02:45 CEST (+02) -> 02:15 CET (+01) is 30 minutes real duration,
        // even though wall-clock end (02:15) is earlier than wall-clock start (02:45).
        var start = new DateTimeOffset(2026, 10, 25, 2, 45, 0, TimeSpan.FromHours(2)).ToUniversalTime();
        var end = new DateTimeOffset(2026, 10, 25, 2, 15, 0, TimeSpan.FromHours(1)).ToUniversalTime();
        Assert.Equal(TimeSpan.FromMinutes(30), end - start);

        var ev = new CalendarEvent
        {
            StartsAtUtc = start,
            EndsAtUtc = end,
            IsAllDay = false,
            Recurrence = RecurrenceFrequency.Daily,
            RecurrenceInterval = 1
        };

        var occurrences = RecurrenceExpander.Expand(
            ev, tz, start.AddHours(-1), start.AddDays(2).AddHours(12), 10).ToList();

        Assert.Equal(3, occurrences.Count);
        foreach (var occ in occurrences)
        {
            Assert.True(occ.EndsAtUtc > occ.StartsAtUtc);
            Assert.Equal(TimeSpan.FromMinutes(30), occ.EndsAtUtc - occ.StartsAtUtc);
        }
    }

    [Fact]
    public void TimedEventLandingInSpringForwardGapNeverProducesInvertedInterval()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        // Starts 2026-03-28 02:45–03:15 CET; next day 2026-03-29 02:45 is inside the 02:00->03:00 DST gap.
        var start = new DateTimeOffset(2026, 3, 28, 2, 45, 0, TimeSpan.FromHours(1)).ToUniversalTime();
        var ev = new CalendarEvent
        {
            StartsAtUtc = start,
            EndsAtUtc = start.AddMinutes(30),
            IsAllDay = false,
            Recurrence = RecurrenceFrequency.Daily,
            RecurrenceInterval = 1
        };

        var occurrences = RecurrenceExpander.Expand(
            ev, tz, start.AddHours(-1), start.AddDays(2).AddHours(12), 10).ToList();

        Assert.Equal(3, occurrences.Count);
        var dstDay = occurrences[1];
        Assert.Equal(new DateOnly(2026, 3, 29), dstDay.Date);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 3, 45, 0, TimeSpan.FromHours(2)).ToUniversalTime(),
            dstDay.StartsAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(30), dstDay.EndsAtUtc - dstDay.StartsAtUtc);
        Assert.True(dstDay.EndsAtUtc > dstDay.StartsAtUtc);
    }

    [Fact]
    public void SpringGapUsesActualZoneDeltaForHalfHourDstZone()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Australia/Lord_Howe");
        // 2026-10-04 clocks jump 02:00 -> 02:30 (+30 min, not +1 hour).
        var start = new DateTimeOffset(2026, 10, 3, 2, 15, 0, TimeSpan.FromMinutes(630)).ToUniversalTime();
        var ev = new CalendarEvent
        {
            StartsAtUtc = start,
            EndsAtUtc = start.AddMinutes(30),
            IsAllDay = false,
            Recurrence = RecurrenceFrequency.Daily,
            RecurrenceInterval = 1
        };

        var occurrences = RecurrenceExpander.Expand(
            ev, tz, start.AddHours(-1), start.AddDays(1).AddHours(12), 10).ToList();

        Assert.Equal(2, occurrences.Count);
        var dstDay = occurrences[1];
        // 02:15 shifted by +30 min = 02:45 UTC+11:00.
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 2, 45, 0, TimeSpan.FromHours(11)).ToUniversalTime(),
            dstDay.StartsAtUtc);
        Assert.Equal(TimeSpan.FromMinutes(30), dstDay.EndsAtUtc - dstDay.StartsAtUtc);
    }

    [Fact]
    public void ExtremeDatesNeverThrowOverflowException()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        var ev = new CalendarEvent
        {
            StartsAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndsAtUtc = new DateTimeOffset(9999, 12, 30, 0, 0, 0, TimeSpan.Zero),
            IsAllDay = false,
            Recurrence = RecurrenceFrequency.Daily,
            RecurrenceInterval = 1
        };

        var occurrences = RecurrenceExpander.Expand(
            ev, tz, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.MaxValue, 10).ToList();

        Assert.NotEmpty(occurrences);
        Assert.All(occurrences, o => Assert.True(o.EndsAtUtc > o.StartsAtUtc));
    }

    [Fact]
    public void OccursOnMatchesLongDurationRecurringEventsWithoutLimit()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        var start = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var ev = new CalendarEvent
        {
            StartsAtUtc = start,
            EndsAtUtc = start.AddDays(45),
            IsAllDay = false,
            Recurrence = RecurrenceFrequency.Daily,
            RecurrenceInterval = 1,
            RecurrenceUntil = new DateOnly(2026, 3, 1)
        };

        Assert.True(RecurrenceExpander.OccursOn(ev, tz, new DateOnly(2026, 2, 14)));
        Assert.False(RecurrenceExpander.OccursOn(ev, tz, new DateOnly(2025, 12, 31)));
        Assert.False(RecurrenceExpander.OccursOn(ev, tz, new DateOnly(2026, 3, 2)));
    }
}