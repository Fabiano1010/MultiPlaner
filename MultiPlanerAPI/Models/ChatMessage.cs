namespace MultiPlanerAPI.Models;

public sealed class ChatMessage
{
    public long Id { get; set; }
    public int RoomId { get; set; }
    public int AuthorMemberId { get; set; }
    public Guid ClientRequestId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset SentAtUtc { get; set; }
}
