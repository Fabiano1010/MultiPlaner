using MultiPlanerSharedModels.JSON;

namespace MultiPlanerAPI.Models;

public class CalendarState
{
    public int CalendarId { get; set; }
    public int UserId { get; set; }
    public CalendarStateContent StateContent { get; set; } = new();

    public Calendar Calendar { get; set; } = null!;
    public User User { get; set; } = null!;
}