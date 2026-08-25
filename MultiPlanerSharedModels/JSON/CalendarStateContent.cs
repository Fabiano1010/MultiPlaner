using System.Text.Json;

namespace MultiPlanerSharedModels.JSON;

public class CalendarStateContent
{
    public string ActiveView { get; set; } = "month";
    public Dictionary<string, JsonElement> CustomFilters { get; set; } = new();}