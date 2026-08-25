namespace MultiPlanerAPI.Models;

public class CalendarList
{
    public int Id { get; set; }
    public int? CalendarSublistId { get; set; }
    public int? UserId { get; set; }
    public int MenuId { get; set; }

    public CalendarList? CalendarSublist { get; set; }
    public ICollection<CalendarList> SubLists { get; set; } = new List<CalendarList>();
    public User? User { get; set; }
    public Menu Menu { get; set; } = null!;
    public ICollection<CalendarCalendarList> CalendarCalendarLists { get; set; } = new List<CalendarCalendarList>();
}