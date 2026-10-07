using System.ComponentModel.DataAnnotations;

namespace MultiPlanerSharedModels.Contracts.Rooms;

public sealed class CreateRoomRequest
{
    [Required, StringLength(128, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [StringLength(128)]
    public string? TimeZoneId { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }
}
