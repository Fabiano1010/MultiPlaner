namespace MultiPlanerAPI.Models;

public class CalendarUser
{
    public int UserId { get; set; }
    public int CalendarId { get; set; }
    public string UserRole { get; set; } = string.Empty;
    public bool IsFavourite { get; set; }
    public DateTime JoinedAt { get; set; }
    public string? UserAlias { get; set; }

    public User User { get; set; } = null!;
    public Calendar Calendar { get; set; } = null!;
}