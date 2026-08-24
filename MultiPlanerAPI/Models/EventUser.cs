namespace MultiPlanerAPI.Models;

public class EventUser
{
    public int UserId { get; set; }
    public int EventId { get; set; }
    public string UserRole { get; set; } = string.Empty;

    public User User { get; set; } = null!;
    public Event Event { get; set; } = null!;
}