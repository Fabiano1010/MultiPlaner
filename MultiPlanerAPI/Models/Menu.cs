namespace MultiPlanerAPI.Models;

public class Menu
{
    public int Id { get; set; }
    public int UserId { get; set; }

    public User User { get; set; } = null!;
    public ICollection<CalendarList> CalendarLists { get; set; } = new List<CalendarList>();
}