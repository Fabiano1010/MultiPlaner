using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Auth;

namespace MultiPlanerAPI.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class GuestSessionTests(ApiFixture fixture)
{
    [Fact]
    public async Task GuestCanReadSessionButCannotEditAccountProfile()
    {
        var (client, id, _) = await fixture.GuestAsync();
        using (client)
        {
            var profile = await client.GetFromJsonAsync<SessionResponse>("/api/me");
            Assert.Equal("guest", profile!.Kind);
            Assert.Null(profile.UserId);
            Assert.Null(profile.Email);
            Assert.Equal(id, profile.GuestSessionId);
            Assert.Equal("Named Guest", profile.DisplayName);
            Assert.InRange(profile.ExpiresAtUtc!.Value, DateTimeOffset.UtcNow.AddDays(29), DateTimeOffset.UtcNow.AddDays(31));
            await ApiFixture.CsrfAsync(client);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PatchAsJsonAsync("/api/me", new { displayName = "Different" })).StatusCode);
        }
    }

    [Fact]
    public async Task ExpiredCookieCannotBeUsed()
    {
        var (client, _, _) = await fixture.GuestAsync(DateTimeOffset.UtcNow.AddMinutes(-1));
        using (client)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task DatabaseRevocationRejectsAnOtherwiseValidCookie()
    {
        var (client, id, _) = await fixture.GuestAsync();
        using (client)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<GuestSessionService>().RevokeAsync(id);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        }
    }

    [Fact]
    public async Task DatabaseExpiryRejectsAnOtherwiseValidCookie()
    {
        var (client, id, _) = await fixture.GuestAsync();
        using (client)
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.GuestSessions.Where(s => s.Id == id).ExecuteUpdateAsync(update => update
                .SetProperty(s => s.CreatedAtUtc, DateTimeOffset.UtcNow.AddDays(-31))
                .SetProperty(s => s.ExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        }
    }

    [Fact]
    public async Task GuestLogoutRevokesCopiedCookie()
    {
        var (client, _, cookie) = await fixture.GuestAsync();
        using (client)
        using (var copy = fixture.Client(handleCookies: false))
        {
            copy.DefaultRequestHeaders.Add("Cookie", cookie);
            await ApiFixture.CsrfAsync(client);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await copy.GetAsync("/api/me")).StatusCode);
        }
    }

    [Fact]
    public async Task AnonymousCallerCannotCreateGuestSessionWithoutInvitation()
    {
        using var client = fixture.Client();
        await ApiFixture.CsrfAsync(client);
        var response = await client.PostAsJsonAsync("/api/auth/guest", new { displayName = "Impersonator" });
        Assert.False(response.IsSuccessStatusCode);
    }
}
