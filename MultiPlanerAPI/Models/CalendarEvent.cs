namespace MultiPlanerAPI.Models;

public class CalendarEvent
{
    public int CalendarId { get; set; }
    public int EventId { get; set; }

    public Calendar Calendar { get; set; } = null!;
    public Event Event { get; set; } = null!;
}