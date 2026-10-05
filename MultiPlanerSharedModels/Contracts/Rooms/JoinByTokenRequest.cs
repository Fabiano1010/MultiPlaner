using System.ComponentModel.DataAnnotations;

namespace MultiPlanerSharedModels.Contracts.Rooms;

public sealed class JoinByTokenRequest
{
    [Required]
    public string Token { get; init; } = string.Empty;

    [StringLength(64, MinimumLength = 2)]
    public string? DisplayName { get; init; }
}
