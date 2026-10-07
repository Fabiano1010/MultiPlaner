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
    /// <summary>A read cursor, not a foreign key, so messages can be deleted independently.</summary>
    public long? LastReadMessageId { get; set; }
    /// <summary>A read cursor, not a foreign key, so activities can be deleted independently.</summary>
    public long? LastReadActivityId { get; set; }
}
