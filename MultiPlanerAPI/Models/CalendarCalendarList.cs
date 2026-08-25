namespace MultiPlanerAPI.Models;

public class CalendarCalendarList
{
    public int CalendarId { get; set; }
    public int CalendarListId { get; set; }

    public Calendar Calendar { get; set; } = null!;
    public CalendarList CalendarList { get; set; } = null!;
}