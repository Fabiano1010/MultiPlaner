namespace MultiPlanerAPI.Models;

public sealed class EventAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int EventId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
