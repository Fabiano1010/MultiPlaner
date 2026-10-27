using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Events;
using MultiPlanerSharedModels.Contracts.Events;

namespace MultiPlanerAPI.UnitTests;

public sealed class EventTimeTests
{
    [Theory]
    [InlineData(2026, 3, 29, 23)]
    [InlineData(2026, 10, 25, 25)]
    [InlineData(2026, 5, 12, 24)]
    public void AllDayUsesActualLocalDayLength(int year, int month, int day, int hours)
    {
        var date = new DateOnly(year, month, day);
        var ev = new CalendarEvent();
        EventTime.Apply(ev, new() { IsAllDay = true, StartDate = date, EndDateExclusive = date.AddDays(1) }, "Europe/Warsaw");
        Assert.Equal(hours, (ev.EndsAtUtc - ev.StartsAtUtc).TotalHours);
        Assert.Equal(date, ev.StartDate);
        Assert.Equal("Europe/Warsaw", ev.TimeZoneId);
        Assert.Equal(TimeSpan.Zero, ev.StartsAtUtc.Offset);
    }

    [Fact]
    public void ExistingAllDayEventKeepsItsZoneAndCanChangeToTimed()
    {
        var ev = new CalendarEvent();
        var time = new EventTimeRequest { IsAllDay = true, StartDate = new(2026, 3, 29), EndDateExclusive = new(2026, 3, 30) };
        EventTime.Apply(ev, time, "Europe/Warsaw");
        var start = ev.StartsAtUtc;
        EventTime.Apply(ev, time, "America/New_York");
        Assert.Equal(start, ev.StartsAtUtc);
        EventTime.Apply(ev, new() { StartsAtUtc = start, EndsAtUtc = start.AddHours(1) }, "America/New_York");
        Assert.False(ev.IsAllDay);
        Assert.Null(ev.StartDate);
        Assert.Null(ev.EndDateExclusive);
        Assert.Null(ev.TimeZoneId);
    }

    [Fact]
    public void TimedEventNormalizesOffsetsAndRejectsEmptyOrMixedIntervals()
    {
        var start = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2));
        var end = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(1));
        var ev = new CalendarEvent();
        EventTime.Apply(ev, new() { StartsAtUtc = start, EndsAtUtc = end }, "Europe/Warsaw");
        Assert.Equal(1, (ev.EndsAtUtc - ev.StartsAtUtc).TotalHours);
        Assert.Equal(TimeSpan.Zero, ev.StartsAtUtc.Offset);
        Assert.Throws<ApiException>(() => EventTime.Apply(ev, new() { StartsAtUtc = start, EndsAtUtc = start }, "UTC"));
        Assert.Throws<ApiException>(() => EventTime.Apply(ev, new() { StartsAtUtc = start, EndsAtUtc = end, StartDate = new(2026, 1, 1) }, "UTC"));
        Assert.Throws<ApiException>(() => EventTime.Apply(ev, new() { IsAllDay = true, StartDate = new(2026, 1, 1), EndDateExclusive = new(2026, 1, 1) }, "UTC"));
    }
}
