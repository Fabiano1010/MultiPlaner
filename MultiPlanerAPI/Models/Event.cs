namespace MultiPlanerAPI.Models;

public class Event
{
    public int Id { get; set; }
    public int UserId { get; set; } 
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsHighPriority { get; set; }

    public User User { get; set; } = null!;
    public EventData? EventData { get; set; }
    public CalendarEvent? CalendarEvent { get; set; }
    public ICollection<EventUser> EventUsers { get; set; } = new List<EventUser>();
}