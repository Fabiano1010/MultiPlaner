namespace MultiPlanerAPI.Models;

public class EventData
{
    public int EventId { get; set; }
    public DateOnly StartingDate { get; set; }
    public DateOnly EndingDate { get; set; }
    public string? Description { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public Event Event { get; set; } = null!;
}