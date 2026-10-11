namespace MultiPlanerAPI.Models;

public sealed class RoomInvitation : TrackedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int RoomId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string? ProtectedToken { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public int? MaxUses { get; set; }
    public int UseCount { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
