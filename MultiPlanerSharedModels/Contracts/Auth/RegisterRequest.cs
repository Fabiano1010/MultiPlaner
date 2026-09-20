using System.ComponentModel.DataAnnotations;

namespace MultiPlanerSharedModels.Contracts.Auth;

public sealed class RegisterRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;

    [Required, StringLength(64, MinimumLength = 2)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, RegularExpression("^[A-Z]{2}$")]
    public string CountryCode { get; init; } = "PL";

    [Required, StringLength(128)]
    public string TimeZoneId { get; init; } = "Europe/Warsaw";
}
