using MultiPlanerSharedModels.Contracts.Events;

namespace MultiPlanerSharedUI.Services;

public static class CalendarRange
{
    public static (DateOnly Start, DateOnly EndExclusive) Grid(DateTime month) {
        var first = new DateOnly(month.Year, month.Month, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7;
        var cells = (int)Math.Ceiling((offset + DateTime.DaysInMonth(month.Year, month.Month)) / 7.0) * 7;
        var start = first.AddDays(-offset);
        return (start, start.AddDays(cells));
    }
}

public static class EventDays
{
    public static (DateOnly First, DateOnly Last) Range(EventResponse e) {
        if (e.IsAllDay && e.StartDate is { } s && e.EndDateExclusive is { } x) {
            var last = x.AddDays(-1);
            return (s, last < s ? s : last);
        }

        var tz = TimeZoneInfo.FindSystemTimeZoneById(e.RoomTimeZoneId);
        var start = TimeZoneInfo.ConvertTime(e.StartsAtUtc, tz);
        var end = TimeZoneInfo.ConvertTime(e.EndsAtUtc, tz);
        var first = DateOnly.FromDateTime(start.DateTime);
        var lastDay = DateOnly.FromDateTime(end.DateTime);
        if (end.TimeOfDay == TimeSpan.Zero && lastDay > first) lastDay = lastDay.AddDays(-1);
        return (first, lastDay);
    }

    public static EventSpan Span(EventResponse e) {
        if (e.IsAllDay && e.StartDate is { } s && e.EndDateExclusive is { } x) {
            var last = x.AddDays(-1);
            return new EventSpan(s, last < s ? s : last, null, null);
        }


        var (startDate, startTime) = ToLocal(e.StartsAtUtc, e.RoomTimeZoneId);
        var (endDate, endTime) = ToLocal(e.EndsAtUtc, e.RoomTimeZoneId);
        return new EventSpan(startDate, endDate, startTime, endTime);
    }

    public static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, string tzId) {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, tz), TimeSpan.Zero);
    }

    public static (DateOnly Date, TimeOnly Time) ToLocal(DateTimeOffset utc, string tzId) {
        var local = TimeZoneInfo.ConvertTime(utc, TimeZoneInfo.FindSystemTimeZoneById(tzId));
        return (DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime));
    }

    public readonly record struct EventSpan(
        DateOnly StartDate,
        DateOnly EndDate,
        TimeOnly? StartTime,
        TimeOnly? EndTime);
}