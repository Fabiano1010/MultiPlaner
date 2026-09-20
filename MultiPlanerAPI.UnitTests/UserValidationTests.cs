using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Auth;
using MultiPlanerSharedModels.Contracts.Users;

namespace MultiPlanerAPI.UnitTests;

public sealed class UserValidationTests
{
    [Theory]
    [InlineData("PL", "Europe/Warsaw")]
    [InlineData("US", "America/New_York")]
    [InlineData("DE", "UTC")]
    public void AcceptsKnownLocales(string country, string zone) =>
        Assert.Empty(LocaleValidation.GetErrors("Anna", country, zone));

    [Theory]
    [InlineData("A", "PL", "Europe/Warsaw", "displayName")]
    [InlineData("  ", "PL", "Europe/Warsaw", "displayName")]
    [InlineData("Anna", "ZZ", "Europe/Warsaw", "countryCode")]
    [InlineData("Anna", "pl", "Europe/Warsaw", "countryCode")]
    [InlineData("Anna", "PL", "Mars/Olympus", "timeZoneId")]
    [InlineData("Anna", "PL", "Central European Standard Time", "timeZoneId")]
    public void RejectsInvalidLocaleOrName(string name, string country, string zone, string field) =>
        Assert.Contains(field, LocaleValidation.GetErrors(name, country, zone).Keys);

    [Fact]
    public void RegistrationRequiresCredentialsAndName()
    {
        var request = new RegisterRequest();
        var errors = Validate(request);
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(RegisterRequest.Email)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(RegisterRequest.Password)));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(RegisterRequest.DisplayName)));
    }

    [Fact]
    public void ProfilePatchRequiresAChange() => Assert.NotEmpty(Validate(new UpdateProfileRequest()));

    [Theory]
    [InlineData("http://example.com/avatar.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    public void AvatarRequiresHttps(string value) =>
        Assert.NotEmpty(Validate(new UpdateProfileRequest { AvatarUrl = value }));

    [Fact]
    public void OptionalProfileFieldsCanBeCleared() =>
        Assert.Empty(Validate(new UpdateProfileRequest { AvatarUrl = "", ContactDetails = "" }));

    [Fact]
    public void GuestCannotBeMistakenForRegisteredAccount()
    {
        var guestId = Guid.NewGuid();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "123"),
                new Claim(SessionAuthentication.GuestIdClaim, guestId.ToString())
            ], SessionAuthentication.GuestScheme))
        };
        var actor = new CurrentActor(new HttpContextAccessor { HttpContext = context });
        Assert.Null(actor.UserId);
        Assert.Equal(guestId, actor.GuestSessionId);
        Assert.Equal(403, Assert.Throws<ApiException>(() => actor.RequireUserId()).StatusCode);
    }

    [Fact]
    public void AnonymousClaimsDoNotGrantIdentity()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "123")]))
        };
        Assert.Null(new CurrentActor(new HttpContextAccessor { HttpContext = context }).UserId);
    }

    private static List<ValidationResult> Validate(object value)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(value, new ValidationContext(value), errors, validateAllProperties: true);
        return errors;
    }
}
