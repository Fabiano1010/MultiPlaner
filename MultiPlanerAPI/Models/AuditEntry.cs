namespace MultiPlanerAPI.Models;

public sealed class AuditEntry
{
    public long Id { get; set; }
    // Deliberately not FKs. Audit metadata can outlive its resource; anonymize on erasure.
    public int? UserId { get; set; }
    public Guid? GuestSessionId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
}
