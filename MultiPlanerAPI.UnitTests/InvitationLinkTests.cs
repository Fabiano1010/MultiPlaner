using Microsoft.Extensions.Configuration;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Rooms;

namespace MultiPlanerAPI.UnitTests;

public sealed class InvitationLinkTests
{
    [Theory]
    [InlineData("https://localhost:7132", "https://localhost:7132/join/abc")]
    [InlineData("https://example.com/calendar/", "https://example.com/calendar/join/abc")]
    public void CreatesShareableWebUrl(string baseUrl, string expected)
    {
        var builder = new InvitationLinkBuilder(Configuration(baseUrl));
        var invitation = new RoomInvitation { RoomId = 7, ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1) };

        var result = builder.CreateResponse(invitation, "abc");

        Assert.Equal(expected, result.JoinUrl);
        Assert.Equal("/api/invitations/abc", result.JoinPath);
        Assert.Equal(7, result.RoomId);
    }

    [Theory]
    [InlineData("http://localhost:7132")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@example.com")]
    public void RejectsUnsafeWebBase(string baseUrl)
    {
        var builder = new InvitationLinkBuilder(Configuration(baseUrl));
        Assert.Throws<InvalidOperationException>(() =>
            builder.CreateResponse(new RoomInvitation { RoomId = 1 }, "abc"));
    }

    private static IConfiguration Configuration(string baseUrl) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Invitations:PublicWebBaseUrl"] = baseUrl
        }).Build();
}
