namespace MultiPlanerSharedModels.Contracts.Rooms;

public sealed record RoomResponse(
    int Id, string Name, string TimeZoneId, int OwnerUserId, int MemberId,
    bool IsOwner, DateTimeOffset ExpiresAtUtc, DateTimeOffset? ArchivedAtUtc,
    RoomInvitationResponse? InitialInvitation = null,
    RoomMembershipResponse? Membership = null,
    bool ArchiveOnExpiry = false);
