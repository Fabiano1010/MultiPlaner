using System.ComponentModel.DataAnnotations;
namespace MultiPlanerSharedModels.Contracts.Users;

public sealed class ChangePasswordRequest
{
    [Required] public string CurrentPassword { get; init; } = "";
    [Required, StringLength(256, MinimumLength = 8)] public string NewPassword { get; init; } = "";
}

public sealed record ThemePalette
{
    public const string HexPattern = "^#[0-9A-Fa-f]{6}$";
    [Required, RegularExpression(HexPattern)] public string Background { get; set; } = "#FFFFFF";
    [Required, RegularExpression(HexPattern)] public string Surface { get; set; } = "#DDDDDD";
    [Required, RegularExpression(HexPattern)] public string Accent { get; set; } = "#000000";
    [Required, RegularExpression(HexPattern)] public string Border { get; set; } = "#999999";
}
public sealed record ThemeResponse(ThemePalette Palette, byte[] RowVersion, int ThemeId = -1);
public sealed record SavedThemeResponse(int Id, string Name, ThemePalette Palette, byte[] RowVersion, bool IsBuiltIn = false);
public sealed class SaveThemeRequest
{
    [Required, StringLength(64, MinimumLength = 1)] public string Name { get; init; } = "";
    [Required] public ThemePalette Palette { get; init; } = new();
    [Required, MaxLength(8)] public byte[] RowVersion { get; init; } = [];
}
public sealed class ApplyThemeRequest
{
    public int ThemeId { get; init; } = -1;
    [Required, MaxLength(8)] public byte[] RowVersion { get; init; } = [];
    [Required, MaxLength(8)] public byte[] ActiveRowVersion { get; init; } = [];
}
public sealed class ResetThemeRequest
{
    [Required, MaxLength(8)] public byte[] RowVersion { get; init; } = [];
}
