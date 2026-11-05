namespace MultiPlanerAPI.Models;

public sealed class PollVote
{
    public int PollId { get; set; }
    public int MemberId { get; set; }
    public int Slot { get; set; }        // 0 - single, OptionId - multiple
    public int OptionId { get; set; }
    public DateTimeOffset CastAtUtc { get; set; }
}