namespace MultiPlanerSharedModels.Models;

public class Event
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string User { get; set; }
    
    public string? Description { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public String Color { get; set; }
    public bool IsHighPriority { get; set; }

}