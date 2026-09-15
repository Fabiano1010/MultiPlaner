namespace MultiPlanerAPI.Models;

public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int? RoomId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
    public Guid? LockId { get; set; }
    public int AttemptCount { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
