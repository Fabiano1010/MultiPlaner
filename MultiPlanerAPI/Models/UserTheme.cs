using MultiPlanerSharedModels.Contracts.Users;
namespace MultiPlanerAPI.Models;
public sealed class UserTheme : TrackedEntity
{
    public int UserId { get; set; }
    public ThemePalette Palette { get; set; } = new();
}
