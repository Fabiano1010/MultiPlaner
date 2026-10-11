namespace MultiPlanerSharedModels.Contracts.Rooms;

public sealed record RoomMembershipResponse(
    int Id, string DisplayName, string Color, bool IsFavourite, byte[] RowVersion);
