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

    public static string Describe(EventResponse e) {
        var (f, l) = Range(e);
        if (e.IsAllDay)
            return f == l ? "All day" : $"{f:dd.MM.yyyy} – {l:dd.MM.yyyy}";

        var tz = TimeZoneInfo.FindSystemTimeZoneById(e.RoomTimeZoneId);
        var s = TimeZoneInfo.ConvertTime(e.StartsAtUtc, tz);
        var en = TimeZoneInfo.ConvertTime(e.EndsAtUtc, tz);
        return f == l ? $"{s:HH:mm} – {en:HH:mm}" : $"{s:dd.MM HH:mm} – {en:dd.MM HH:mm}";
    }
}