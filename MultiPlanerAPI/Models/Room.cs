namespace MultiPlanerAPI.Models;

public sealed class Room : TrackedEntity
{
    public int Id { get; set; }
    public int OwnerUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "Europe/Warsaw";
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ArchivedAtUtc { get; set; }
}
