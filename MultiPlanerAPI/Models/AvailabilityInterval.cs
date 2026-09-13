namespace MultiPlanerAPI.Models;

public sealed class AvailabilityInterval : TrackedEntity
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public int MemberId { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset EndsAtUtc { get; set; }
}
