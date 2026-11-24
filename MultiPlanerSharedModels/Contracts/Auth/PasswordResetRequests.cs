using System.ComponentModel.DataAnnotations;

namespace MultiPlanerSharedModels.Contracts.Auth;

public sealed class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; init; } = string.Empty;
}

public sealed class ResetPasswordRequest
{
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required] public string Token { get; init; } = string.Empty;

    [Required]
    [StringLength(PasswordLimits.MaxLength, MinimumLength = PasswordLimits.MinLength)]
    public string NewPassword { get; init; } = string.Empty;
}