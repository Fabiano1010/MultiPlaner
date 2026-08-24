namespace MultiPlanerAPI.Models;

public class CalendarPoll
{
    public int IdCalendar { get; set; }
    public int IdPoll { get; set; }

    public Calendar Calendar { get; set; } = null!;
    public Poll Poll { get; set; } = null!;
}