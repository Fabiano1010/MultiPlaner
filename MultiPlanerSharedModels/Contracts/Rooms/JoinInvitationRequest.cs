using System.ComponentModel.DataAnnotations;

namespace MultiPlanerSharedModels.Contracts.Rooms;

public sealed class JoinInvitationRequest
{
    [StringLength(64, MinimumLength = 2)]
    public string? DisplayName { get; init; }
}
