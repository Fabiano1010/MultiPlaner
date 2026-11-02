namespace MultiPlanerSharedModels.Contracts.Rooms;

public sealed record RoomMemberResponse(
    int Id, string DisplayName, string Color, bool IsGuest, bool IsOwner,
    DateTimeOffset JoinedAtUtc, bool IsAdmin = false, string? AvatarUrl = null,
    byte[]? RowVersion = null, bool CanRemove = false, bool CanPromote = false, bool CanDemote = false);
