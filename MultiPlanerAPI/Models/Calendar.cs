namespace MultiPlanerAPI.Models;

public class Calendar
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ImageLink { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string MagicLink { get; set; } = string.Empty;

    public MessageRoom? MessageRoom { get; set; }
    public ICollection<CalendarEvent> CalendarEvents { get; set; } = new List<CalendarEvent>();
    public ICollection<CalendarPoll> CalendarPolls { get; set; } = new List<CalendarPoll>();
    public ICollection<CalendarState> CalendarStates { get; set; } = new List<CalendarState>();
    public ICollection<CalendarUser> CalendarUsers { get; set; } = new List<CalendarUser>();
    public ICollection<CalendarCalendarList> CalendarCalendarLists { get; set; } = new List<CalendarCalendarList>();
}