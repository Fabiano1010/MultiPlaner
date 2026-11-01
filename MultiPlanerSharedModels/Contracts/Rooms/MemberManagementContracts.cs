using System.ComponentModel.DataAnnotations;
namespace MultiPlanerSharedModels.Contracts.Rooms;
public sealed class ChangeMemberRoleRequest
{
    public bool IsAdmin { get; init; }
    [Required, MinLength(8), MaxLength(8)] public byte[] RowVersion { get; init; } = [];
}
public sealed class RemoveMemberRequest
{
    [Required, MinLength(8), MaxLength(8)] public byte[] RowVersion { get; init; } = [];
}
