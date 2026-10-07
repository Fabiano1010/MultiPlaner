namespace MultiPlanerSharedModels.Contracts.Rooms;

public sealed class CreateRoomInvitationRequest
{
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public int? MaxUses { get; init; }
}
