using System.ComponentModel.DataAnnotations;

namespace MultiPlanerSharedModels.Contracts.Events;

public sealed class CreateEventTypeRequest
{
    [Required]
    [StringLength(64, MinimumLength = 1)]
    public string Name { get; init; } = "";

    [Required]
    [RegularExpression("^[0-9A-Fa-f]{6}$")]
    public string Color { get; init; } = "999999";
}

public sealed class UpdateEventTypeRequest
{
    [StringLength(64, MinimumLength = 1)] public string? Name { get; init; }

    [RegularExpression("^[0-9A-Fa-f]{6}$")]
    public string? Color { get; init; }

    public bool? IsArchived { get; init; }

    [Required]
    [MinLength(8)]
    [MaxLength(8)]
    public byte[] RowVersion { get; init; } = [];
}

public sealed record EventTypeResponse(
    int Id,
    int RoomId,
    string Name,
    string Color,
    bool IsArchived,
    byte[] RowVersion);