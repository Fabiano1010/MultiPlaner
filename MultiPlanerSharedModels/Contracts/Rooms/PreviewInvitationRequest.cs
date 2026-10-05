using System.ComponentModel.DataAnnotations;

namespace MultiPlanerSharedModels.Contracts.Rooms;

public sealed class PreviewInvitationRequest
{
    [Required]
    public string Token { get; init; } = string.Empty;
}
