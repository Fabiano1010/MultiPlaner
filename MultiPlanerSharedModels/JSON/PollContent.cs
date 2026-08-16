namespace MultiPlanerSharedModels.JSON;

public class PollContent
{
    public string Question { get; set; } = string.Empty;
    public List<string> Options { get; set; } = new();
}