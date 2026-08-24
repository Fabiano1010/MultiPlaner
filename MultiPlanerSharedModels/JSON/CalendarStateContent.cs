namespace MultiPlanerSharedModels.JSON;

public class CalendarStateContent
{
    public string ActiveView { get; set; } = "month";
    public Dictionary<string, object> CustomFilters { get; set; } = new();
}