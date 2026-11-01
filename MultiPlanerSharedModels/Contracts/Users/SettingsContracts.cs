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
    [Required, RegularExpression(HexPattern)] public string Primary { get; set; } = "#999999";
    [Required, RegularExpression(HexPattern)] public string PanelBackground { get; set; } = "#CCCCCC";
    [Required, RegularExpression(HexPattern)] public string ControlBackground { get; set; } = "#DDDDDD";
    [Required, RegularExpression(HexPattern)] public string Secondary { get; set; } = "#000000";
    [Required, RegularExpression(HexPattern)] public string Background { get; set; } = "#FFFFFF";
    [Required, RegularExpression(HexPattern)] public string RoomSelection { get; set; } = "#43D5FF";
    [Required, RegularExpression(HexPattern)] public string DaySelection { get; set; } = "#6200EE";
    [Required, RegularExpression(HexPattern)] public string DaySelectionBackground { get; set; } = "#EDE7F6";
}
public sealed record ThemeResponse(ThemePalette Palette, byte[] RowVersion);
public sealed class SaveThemeRequest
{
    [Required] public ThemePalette Palette { get; init; } = new();
    // Empty version means the first save of a previously unconfigured account.
    [Required, MaxLength(8)] public byte[] RowVersion { get; init; } = [];
}
public sealed class ResetThemeRequest
{
    [Required, MaxLength(8)] public byte[] RowVersion { get; init; } = [];
}
