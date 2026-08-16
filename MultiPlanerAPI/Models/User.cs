namespace MultiPlanerAPI.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UserAvatar { get; set; } = string.Empty;

    public UserSettings? UserSettings { get; set; }
    public Menu? Menu { get; set; }
    public ICollection<Event> CreatedEvents { get; set; } = new List<Event>();
    public ICollection<CalendarUser> CalendarUsers { get; set; } = new List<CalendarUser>();
    public ICollection<EventUser> EventUsers { get; set; } = new List<EventUser>();
    public ICollection<PollUser> PollUsers { get; set; } = new List<PollUser>();
    public ICollection<CalendarState> CalendarStates { get; set; } = new List<CalendarState>();
    public ICollection<CalendarList> CalendarLists { get; set; } = new List<CalendarList>();
}