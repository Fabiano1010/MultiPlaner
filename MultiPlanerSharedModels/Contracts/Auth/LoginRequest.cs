using System.ComponentModel.DataAnnotations;

namespace MultiPlanerSharedModels.Contracts.Auth;

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; init; } = string.Empty;
}
