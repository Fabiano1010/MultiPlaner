namespace MultiPlanerSharedModels.Contracts.Rooms;

public sealed record RoomInvitationResponse(
    Guid Id, int RoomId, string JoinPath, string JoinUrl, DateTimeOffset ExpiresAtUtc, int? MaxUses);

public sealed record InvitationPreviewResponse(
    int RoomId, string RoomName, DateTimeOffset ExpiresAtUtc);
