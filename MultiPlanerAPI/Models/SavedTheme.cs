using MultiPlanerSharedModels.Contracts.Users;

namespace MultiPlanerAPI.Models;

public sealed class SavedTheme : TrackedEntity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public ThemePalette Palette { get; set; } = new();
}