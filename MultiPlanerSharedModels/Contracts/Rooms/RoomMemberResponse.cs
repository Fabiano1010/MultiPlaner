namespace MultiPlanerSharedModels.Contracts.Rooms;

public sealed record RoomMemberResponse(
    int Id, string DisplayName, string Color, bool IsGuest, bool IsOwner,
    DateTimeOffset JoinedAtUtc);
