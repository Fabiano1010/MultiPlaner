namespace MultiPlanerAPI.Models;

public sealed class RoomActivity
{
    public long Id { get; set; }
    public int RoomId { get; set; }
    public int? ActorMemberId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
}
