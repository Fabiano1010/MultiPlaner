using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Auth;
using MultiPlanerSharedModels.Contracts.Users;

namespace MultiPlanerAPI.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AccountTests(ApiFixture fixture)
{
    [Fact]
    public async Task RegisterLoginAndReadProfileUseHashedPasswordAndSecureCookie()
    {
        var (client, user, cookie) = await fixture.RegisterAndLoginAsync();
        using (client)
        {
            var profile = await client.GetFromJsonAsync<SessionResponse>("/api/me");
            Assert.Equal(user.UserId, profile!.UserId);
            Assert.Equal("user", profile.Kind);
            Assert.Equal("PL", profile.CountryCode);
            Assert.Equal("Europe/Warsaw", profile.TimeZoneId);
            Assert.Null(profile.GuestSessionId);
            Assert.StartsWith(SessionAuthentication.UserCookie, cookie);

            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.Users.SingleAsync(u => u.Id == user.UserId);
            Assert.NotEqual(ApiFixture.Password, entity.PasswordHash);
            Assert.NotEqual(PasswordVerificationResult.Failed,
                scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>()
                    .VerifyHashedPassword(entity, entity.PasswordHash!, ApiFixture.Password));
            Assert.NotEqual(default, entity.CreatedAtUtc);

            var response = await client.GetAsync("/api/me");
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("securityStamp", json, StringComparison.OrdinalIgnoreCase);
            Assert.True(response.Headers.CacheControl!.NoStore);
            foreach (var header in response.Headers.GetValues("Set-Cookie")
                         .Where(c => c.StartsWith(SessionAuthentication.UserCookie, StringComparison.Ordinal)))
            {
                Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task RegistrationWithoutCsrfIsRejectedWithProblemDetails()
    {
        using var client = fixture.Client();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = "no-csrf@example.com", Password = ApiFixture.Password, DisplayName = "Example"
        });
        await AssertProblem(response, HttpStatusCode.BadRequest, "invalid_csrf");
    }

    [Fact]
    public async Task DuplicateEmailIsCaseInsensitiveIncludingConcurrentRegistration()
    {
        using var first = fixture.Client();
        using var second = fixture.Client();
        await ApiFixture.CsrfAsync(first);
        await ApiFixture.CsrfAsync(second);
        var email = $"duplicate-{Guid.NewGuid():N}@example.com";
        var responses = await Task.WhenAll(
            first.PostAsJsonAsync("/api/auth/register", new RegisterRequest { Email = email, Password = ApiFixture.Password, DisplayName = "First User" }),
            second.PostAsJsonAsync("/api/auth/register", new RegisterRequest { Email = email.ToUpperInvariant(), Password = ApiFixture.Password, DisplayName = "Second User" }));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        await AssertProblem(Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict),
            HttpStatusCode.Conflict, "email_already_registered");
    }

    [Fact]
    public async Task InvalidRegistrationDoesNotCreateAnAccount()
    {
        using var client = fixture.Client();
        await ApiFixture.CsrfAsync(client);
        var email = $"invalid-{Guid.NewGuid():N}@example.com";
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email, Password = ApiFixture.Password, DisplayName = "Person", CountryCode = "ZZ", TimeZoneId = "Mars/Olympus"
        });
        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(u => u.Email == email));
    }

    [Fact]
    public async Task WeakPasswordIsRejectedByIdentity()
    {
        using var client = fixture.Client();
        await ApiFixture.CsrfAsync(client);
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = $"weak-{Guid.NewGuid():N}@example.com", Password = "onlylowercaseletters", DisplayName = "Person"
        });
        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Fact]
    public async Task LoginLocksAccountAfterFiveFailedAttempts()
    {
        var (registeredClient, user, _) = await fixture.RegisterAndLoginAsync();
        registeredClient.Dispose();
        using var attacker = fixture.Client();
        await ApiFixture.CsrfAsync(attacker);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await attacker.PostAsJsonAsync("/api/auth/login", new LoginRequest
            {
                Email = user.Email!, Password = "Wrong-Password9!"
            });
            await AssertProblem(response, HttpStatusCode.Unauthorized, "invalid_credentials");
        }
        var locked = await attacker.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = user.Email!, Password = ApiFixture.Password
        });
        await AssertProblem(locked, HttpStatusCode.Unauthorized, "invalid_credentials");
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var account = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(u => u.Id == user.UserId);
        Assert.True(account.LockoutEnd > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task LogoutInvalidatesCopiedCookieAndOtherAccountSessions()
    {
        var (client, _, cookie) = await fixture.RegisterAndLoginAsync();
        using (client)
        using (var copied = fixture.Client(handleCookies: false))
        {
            copied.DefaultRequestHeaders.Add("Cookie", cookie);
            Assert.Equal(HttpStatusCode.OK, (await copied.GetAsync("/api/me")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await copied.GetAsync("/api/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        }
    }

    [Fact]
    public async Task ProfilePatchChangesOnlyCurrentUserAndCanClearOptionalFields()
    {
        var (client, user, _) = await fixture.RegisterAndLoginAsync();
        using (client)
        {
            var updated = await client.PatchAsJsonAsync("/api/me", new UpdateProfileRequest
            {
                DisplayName = "  New Name  ", CountryCode = "US", TimeZoneId = "America/New_York",
                ContactDetails = "Contact me here", AvatarUrl = "https://example.com/avatar.png"
            });
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            var profile = await updated.Content.ReadFromJsonAsync<SessionResponse>();
            Assert.Equal(user.UserId, profile!.UserId);
            Assert.Equal(user.Email, profile.Email);
            Assert.Equal("New Name", profile.DisplayName);
            Assert.Equal("US", profile.CountryCode);
            var cleared = await client.PatchAsJsonAsync("/api/me", new UpdateProfileRequest { ContactDetails = "", AvatarUrl = "" });
            var final = await cleared.Content.ReadFromJsonAsync<SessionResponse>();
            Assert.Null(final!.ContactDetails);
            Assert.Null(final.AvatarUrl);
            Assert.Equal("New Name", final.DisplayName);
        }
    }

    [Fact]
    public async Task ProfileRejectsForgedIdentityFieldsAndMissingCsrf()
    {
        var (client, _, _) = await fixture.RegisterAndLoginAsync();
        using (client)
        {
            await AssertProblem(await client.PatchAsJsonAsync("/api/me", new { displayName = "Attacker", userId = 1, role = "Owner" }),
                HttpStatusCode.BadRequest, "validation_failed");
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
            await AssertProblem(await client.PatchAsJsonAsync("/api/me", new { displayName = "Other Name" }),
                HttpStatusCode.BadRequest, "invalid_csrf");
        }
    }

    [Fact]
    public async Task AnonymousAccessReturns401WithoutRedirect()
    {
        using var client = fixture.Client();
        var response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CorsDoesNotReflectUntrustedOrigin()
    {
        using var client = fixture.Client();
        var allowed = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        allowed.Headers.Add("Origin", "https://client.example");
        allowed.Headers.Add("Access-Control-Request-Method", "POST");
        var allowedResponse = await client.SendAsync(allowed);
        Assert.Equal("https://client.example", Assert.Single(allowedResponse.Headers.GetValues("Access-Control-Allow-Origin")));
        var denied = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        denied.Headers.Add("Origin", "https://attacker.example");
        denied.Headers.Add("Access-Control-Request-Method", "POST");
        Assert.False((await client.SendAsync(denied)).Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task SwaggerContainsOnlyImplementedAccountEndpoints()
    {
        using var client = fixture.Client();
        var json = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        var paths = json.GetProperty("paths").EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(new[] { "/api/auth/csrf", "/api/auth/login", "/api/auth/logout", "/api/auth/register", "/api/me" }, paths);
    }

    internal static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
    }
}
