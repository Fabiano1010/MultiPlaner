namespace MultiPlanerAPI.Models;

/// <summary>Stores audit metadata independently of resources that may later be deleted or anonymized.</summary>
public sealed class AuditEntry
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public Guid? GuestSessionId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
}
