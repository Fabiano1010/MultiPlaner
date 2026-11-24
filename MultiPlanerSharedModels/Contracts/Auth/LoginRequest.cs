using System.ComponentModel.DataAnnotations;

namespace MultiPlanerSharedModels.Contracts.Auth;

public sealed class LoginRequest
{
    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(PasswordLimits.MaxLength)]
    public string Password { get; init; } = string.Empty;

    public bool RememberMe { get; init; }
}