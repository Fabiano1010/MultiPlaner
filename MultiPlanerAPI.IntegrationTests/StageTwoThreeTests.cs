using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Modules.Rooms;
using MultiPlanerSharedModels.Contracts.Auth;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class StageTwoThreeTests(ApiFixture fixture)
{
    [Fact]
    public async Task RememberMeControlsCookieAndResetInvalidatesSessionAndToken()
    {
        var (client, user, cookie) = await fixture.RegisterAndLoginAsync();
        using (client)
        {
            var email = user.Email!;
            var sessionResponse = await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest { Email = email, Password = ApiFixture.Password });
            Assert.DoesNotContain("expires=", UserCookie(sessionResponse), StringComparison.OrdinalIgnoreCase);

            var persistentResponse = await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest { Email = email, Password = ApiFixture.Password, RememberMe = true });
            Assert.Contains("expires=", UserCookie(persistentResponse), StringComparison.OrdinalIgnoreCase);
            await ApiFixture.CsrfAsync(client);

            var sentBefore = fixture.Factory.ResetEmails.Messages.Length;
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/forgot-password",
                new ForgotPasswordRequest { Email = "nonexistent@example.com" })).StatusCode);
            Assert.Equal(sentBefore, fixture.Factory.ResetEmails.Messages.Length);
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/auth/forgot-password",
                new ForgotPasswordRequest { Email = email })).StatusCode);
            var link = fixture.Factory.ResetEmails.Messages[^1].Link;
            var values = QueryHelpers.ParseQuery(new Uri(link).Query);
            Assert.Equal(email, values["email"].ToString());
            var token = values["token"].ToString();

            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/reset-password",
                new ResetPasswordRequest { Email = email, Token = token, NewPassword = "NewCorrect-Horse9!Battery" })).StatusCode);
            await ApiFixture.CsrfAsync(client);
            await AccountTests.AssertProblem(await client.PostAsJsonAsync("/api/auth/reset-password",
                new ResetPasswordRequest { Email = email, Token = token, NewPassword = "AgainCorrect-Horse9!Battery" }),
                HttpStatusCode.BadRequest, "invalid_reset_token");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest { Email = email, Password = ApiFixture.Password })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest { Email = email, Password = "NewCorrect-Horse9!Battery" })).StatusCode);
        }
    }

    [Fact]
    public async Task OwnerOnlyFoldersArePrivateAndRemovingFolderKeepsRoom()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        var (other, _, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        using (other)
        {
            var room = await CreateRoomAsync(owner);
            var response = await owner.PostAsJsonAsync("/api/me/folders", new CreateUserFolderRequest { Name = "Team" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var folder = (await response.Content.ReadFromJsonAsync<UserFolderResponse>())!;
            Assert.Equal(HttpStatusCode.NoContent,
                (await owner.PutAsync($"/api/me/folders/{folder.Id}/rooms/{room.Id}", null)).StatusCode);
            var listed = (await owner.GetFromJsonAsync<UserFolderResponse[]>("/api/me/folders"))!;
            Assert.Contains(room.Id, Assert.Single(listed).RoomIds);
            Assert.Empty((await other.GetFromJsonAsync<UserFolderResponse[]>("/api/me/folders"))!);
            Assert.Equal(HttpStatusCode.NotFound,
                (await other.PutAsync($"/api/me/folders/{folder.Id}/rooms/{room.Id}", null)).StatusCode);
            var updatedResponse = await owner.PatchAsJsonAsync($"/api/me/folders/{folder.Id}",
                new UpdateUserFolderRequest { IsFavourite = true, RowVersion = folder.RowVersion });
            Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
            var updated = (await updatedResponse.Content.ReadFromJsonAsync<UserFolderResponse>())!;
            Assert.True(updated.IsFavourite);
            await AccountTests.AssertProblem(await owner.PatchAsJsonAsync($"/api/me/folders/{folder.Id}",
                new UpdateUserFolderRequest { Name = "Stale", RowVersion = folder.RowVersion }),
                HttpStatusCode.Conflict, "concurrency_conflict");
            Assert.Equal(HttpStatusCode.NoContent,
                (await owner.DeleteAsync($"/api/me/folders/{folder.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/rooms/{room.Id}")).StatusCode);
        }
    }

    [Fact]
    public async Task InvitationCanBeRecopiedUntilRotationAndRoomVersionProtectsEdits()
    {
        var (owner, user, _) = await fixture.RegisterAndLoginAsync();
        var (other, _, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        using (other)
        {
            var room = await CreateRoomAsync(owner);
            Assert.Equal(user.DisplayName, room.OwnerDisplayName);
            Assert.Equal(room.InitialInvitation!.JoinUrl,
                (await owner.GetFromJsonAsync<RoomInvitationResponse>(
                    $"/api/rooms/{room.Id}/invitations/current"))!.JoinUrl);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await other.GetAsync($"/api/rooms/{room.Id}/invitations/current")).StatusCode);
            var updatedResponse = await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}",
                new UpdateRoomRequest { Name = "Changed", RowVersion = room.RowVersion! });
            Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
            await AccountTests.AssertProblem(await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}",
                new UpdateRoomRequest { Name = "Stale", RowVersion = room.RowVersion! }),
                HttpStatusCode.Conflict, "concurrency_conflict");
            var rotated = await owner.PostAsJsonAsync($"/api/rooms/{room.Id}/invitations",
                new CreateRoomInvitationRequest());
            Assert.Equal(HttpStatusCode.Created, rotated.StatusCode);
            var current = (await owner.GetFromJsonAsync<RoomInvitationResponse>(
                $"/api/rooms/{room.Id}/invitations/current"))!;
            Assert.Equal((await rotated.Content.ReadFromJsonAsync<RoomInvitationResponse>())!.JoinUrl, current.JoinUrl);
            await AccountTests.AssertProblem(await owner.GetAsync(room.InitialInvitation.JoinPath),
                HttpStatusCode.Gone, "invitation_unavailable");
        }
    }

    [Fact]
    public async Task CoverIsReadableToMembersAndWritableOnlyByOwner()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        var (other, _, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        using (other)
        {
            var room = await CreateRoomAsync(owner);
            var png = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL/nwAAAABJRU5ErkJggg==");
            using var multipart = new MultipartFormDataContent();
            multipart.Add(new ByteArrayContent(png), "file", "cover.png");
            Assert.Equal(HttpStatusCode.Forbidden,
                (await other.PutAsync($"/api/rooms/{room.Id}/cover", multipart)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await owner.PutAsync($"/api/rooms/{room.Id}/cover", multipart)).StatusCode);
            Assert.True((await owner.GetFromJsonAsync<RoomResponse>($"/api/rooms/{room.Id}"))!.HasCover);
            using var guest = fixture.Client();
            await ApiFixture.CsrfAsync(guest);
            Assert.Equal(HttpStatusCode.OK, (await guest.PostAsJsonAsync("/api/invitations/join",
                new JoinByTokenRequest
                {
                    Token = room.InitialInvitation!.JoinPath.Split('/').Last(), DisplayName = "Cover Guest"
                })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync($"/api/rooms/{room.Id}/cover")).StatusCode);
            var response = await owner.GetAsync($"/api/rooms/{room.Id}/cover");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal(png, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(HttpStatusCode.NotFound,
                (await other.GetAsync($"/api/rooms/{room.Id}/cover")).StatusCode);
            using var invalid = new MultipartFormDataContent();
            invalid.Add(new ByteArrayContent("This is not a picture"u8.ToArray()), "file", "fake.png");
            await AccountTests.AssertProblem(await owner.PutAsync($"/api/rooms/{room.Id}/cover", invalid),
                HttpStatusCode.BadRequest, "validation_failed");
            Assert.Equal(HttpStatusCode.NoContent,
                (await owner.DeleteAsync($"/api/rooms/{room.Id}/cover")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await owner.GetAsync($"/api/rooms/{room.Id}/cover")).StatusCode);

            using var replacement = new MultipartFormDataContent();
            replacement.Add(new ByteArrayContent(png), "file", "cover.png");
            Assert.Equal(HttpStatusCode.NoContent,
                (await owner.PutAsync($"/api/rooms/{room.Id}/cover", replacement)).StatusCode);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var key = await db.Rooms.Where(r => r.Id == room.Id).Select(r => r.CoverStorageKey).SingleAsync();
            Assert.Equal(HttpStatusCode.NoContent,
                (await owner.DeleteAsync($"/api/rooms/{room.Id}")).StatusCode);
            Assert.Throws<FileNotFoundException>(() => scope.ServiceProvider.GetRequiredService<IRoomCoverStorage>()
                .OpenRead(key!));
        }
    }

    private static async Task<RoomResponse> CreateRoomAsync(HttpClient client)
    {
        var result = await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest { Name = "Stage Test" });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        return (await result.Content.ReadFromJsonAsync<RoomResponse>())!;
    }

    private static string UserCookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie")
        .Single(c => c.StartsWith(MultiPlanerAPI.Modules.Users.SessionAuthentication.UserCookie + "=", StringComparison.Ordinal));
}
