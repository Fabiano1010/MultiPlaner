using System.ComponentModel.DataAnnotations;

namespace MultiPlanerSharedModels.Contracts.Rooms;

public sealed class UpdateRoomRequest
{
    [StringLength(128, MinimumLength = 2)]
    public string? Name { get; init; }

    [StringLength(128)]
    public string? TimeZoneId { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }

    [Required, MinLength(8), MaxLength(8)]
    public byte[] RowVersion { get; init; } = [];
}
