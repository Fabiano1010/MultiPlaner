namespace MauiApp1.Models;

public class DayCell
{
    public int Day { get; set; }
    public bool IsCurrentMonth { get; set; }
    public bool IsSelected { get; set; }
    public bool IsInRange { get; set; }
    public bool HasEvent { get; set; }
}