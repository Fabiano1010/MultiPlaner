using System.ComponentModel.DataAnnotations;

namespace MultiPlanerSharedModels.Contracts.Rooms;

public sealed class UpdateRoomMembershipRequest
{
    [StringLength(64, MinimumLength = 2)]
    public string? DisplayName { get; init; }

    [StringLength(6, MinimumLength = 6), RegularExpression("^[0-9A-Fa-f]{6}$")]
    public string? Color { get; init; }

    public bool? IsFavourite { get; init; }

    [Required, MinLength(8), MaxLength(8)]
    public byte[] RowVersion { get; init; } = [];
}
