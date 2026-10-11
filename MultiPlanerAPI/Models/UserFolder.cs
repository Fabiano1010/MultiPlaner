namespace MultiPlanerAPI.Models;

public sealed class UserFolder : TrackedEntity
{
    public int Id { get; set; }
    public int OwnerUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsFavourite { get; set; }
}

public sealed class UserFolderRoom
{
    public int OwnerUserId { get; set; }
    public int RoomId { get; set; }
    public int FolderId { get; set; }
}
