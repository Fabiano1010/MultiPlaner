namespace MultiPlanerAPI.Models;

public sealed class RoomEventType : TrackedEntity
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string Color { get; set; } = "999999";
    public bool IsArchived { get; set; }
}