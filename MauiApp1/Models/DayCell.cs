namespace MauiApp1.Models;

public class DayCell
{
    public int Day { get; set; }
    public DateTime Date { get; set; }
    public bool IsCurrentMonth { get; set; }
    public bool IsSelected { get; set; }
    public bool IsRangeStart { get; set; }
    public bool IsRangeEnd { get; set; }
    public bool IsInRange { get; set; }
    public bool HasEvent { get; set; }
    public bool IsToday => Date.Date == DateTime.Today;
    public bool IsSunday { get; set; }
}