namespace MultiPlanerAPI.Models;

public sealed class PollOption
{
    public int Id { get; set; }
    public int PollId { get; set; }
    public int Position { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Color { get; set; } = "999999";
}
