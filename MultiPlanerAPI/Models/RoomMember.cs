namespace MultiPlanerAPI.Models;

public sealed class RoomMember : TrackedEntity
{
    public int Id { get; set; }
    public int RoomId { get; set; }
    public int? UserId { get; set; }
    public Guid? GuestSessionId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Color { get; set; } = "808080";
    public bool IsFavourite { get; set; }
    // Read cursors, not foreign keys: deleting a message must not prevent deletion.
    public long? LastReadMessageId { get; set; }
    public long? LastReadActivityId { get; set; }
}
