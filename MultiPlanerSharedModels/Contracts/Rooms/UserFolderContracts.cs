using System.ComponentModel.DataAnnotations;

namespace MultiPlanerSharedModels.Contracts.Rooms;

public sealed record UserFolderResponse(int Id, string Name, bool IsFavourite, byte[] RowVersion, IReadOnlyList<int> RoomIds);

public sealed class CreateUserFolderRequest
{
    [Required, StringLength(128, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;
}

public sealed class UpdateUserFolderRequest
{
    [StringLength(128, MinimumLength = 2)]
    public string? Name { get; init; }
    public bool? IsFavourite { get; init; }
    [Required, MinLength(8), MaxLength(8)]
    public byte[] RowVersion { get; init; } = [];
}
