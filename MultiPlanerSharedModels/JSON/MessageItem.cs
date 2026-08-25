namespace MultiPlanerSharedModels.JSON;

public class MessageItem
{
    public int SenderId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
}