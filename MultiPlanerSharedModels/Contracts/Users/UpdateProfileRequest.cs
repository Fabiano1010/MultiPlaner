using System.ComponentModel.DataAnnotations;

namespace MultiPlanerSharedModels.Contracts.Users;

public sealed class UpdateProfileRequest : IValidatableObject
{
    [StringLength(64, MinimumLength = 2)]
    public string? DisplayName { get; init; }

    [RegularExpression("^[A-Z]{2}$")]
    public string? CountryCode { get; init; }

    [StringLength(128, MinimumLength = 1)]
    public string? TimeZoneId { get; init; }

    [StringLength(2048)]
    public string? AvatarUrl { get; init; }

    [StringLength(256)]
    public string? ContactDetails { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DisplayName is null && CountryCode is null && TimeZoneId is null &&
            AvatarUrl is null && ContactDetails is null)
            yield return new ValidationResult("Provide at least one profile field.");

        if (DisplayName is not null && DisplayName.Trim().Length < 2)
            yield return new ValidationResult("Display name must contain at least two characters.", [nameof(DisplayName)]);

        // An empty string clears these optional fields; null leaves them unchanged.
        if (!string.IsNullOrEmpty(AvatarUrl) &&
            (!Uri.TryCreate(AvatarUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            yield return new ValidationResult("Avatar URL must use HTTPS.", [nameof(AvatarUrl)]);
    }
}
