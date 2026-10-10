using System.Net;
using System.Net.Http.Json;
using MultiPlanerSharedModels.Contracts.Common;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class RoomMembershipTests(ApiFixture fixture)
{
    [Fact]
    public async Task GuestCanUpdateOwnRoomSettingsWithoutChangingOwnerOrAnotherRoom()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        using (var guest = fixture.Client())
        {
            var room = await CreateAsync(owner);
            var secondRoom = await CreateAsync(owner);
            await ApiFixture.CsrfAsync(guest);
            await JoinAsync(guest, room);
            await ApiFixture.CsrfAsync(guest);
            await JoinAsync(guest, secondRoom);
            var initial = await GetAsync(guest, room.Id);
            var result = await guest.PatchAsJsonAsync($"/api/rooms/{room.Id}/members/me",
                new UpdateRoomMembershipRequest
                {
                    DisplayName = "  Local Alias  ", Color = "aabbcc", IsFavourite = true,
                    RowVersion = initial.RowVersion
                });
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            var updated = (await result.Content.ReadFromJsonAsync<RoomMembershipResponse>())!;
            Assert.Equal("Local Alias", updated.DisplayName);
            Assert.Equal("AABBCC", updated.Color);
            Assert.True(updated.IsFavourite);
            Assert.False(initial.RowVersion.SequenceEqual(updated.RowVersion));

            var listed = (await guest.GetFromJsonAsync<RoomResponse[]>("/api/rooms"))!;
            Assert.True(Assert.Single(listed, r => r.Id == room.Id).Membership!.IsFavourite);
            Assert.False((await GetAsync(guest, secondRoom.Id)).IsFavourite);
            Assert.Equal("Guest Name", (await GetAsync(guest, secondRoom.Id)).DisplayName);
            Assert.False((await GetAsync(owner, room.Id)).IsFavourite);
            var members = (await owner.GetFromJsonAsync<PagedResponse<RoomMemberResponse>>(
                $"/api/rooms/{room.Id}/members"))!;
            Assert.Equal("Local Alias", Assert.Single(members.Items, m => m.Id == initial.Id).DisplayName);
        }
    }

    [Fact]
    public async Task OutsiderAndStaleFormCannotChangeMembership()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        var (outsider, _, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        using (outsider)
        {
            var room = await CreateAsync(owner);
            var initial = await GetAsync(owner, room.Id);
            var request = new UpdateRoomMembershipRequest { IsFavourite = true, RowVersion = initial.RowVersion };
            Assert.Equal(HttpStatusCode.NotFound,
                (await outsider.GetAsync($"/api/rooms/{room.Id}/members/me")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await outsider.PatchAsJsonAsync($"/api/rooms/{room.Id}/members/me", request)).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}/members/me", request)).StatusCode);
            await AccountTests.AssertProblem(await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}/members/me",
                new UpdateRoomMembershipRequest { DisplayName = "Stale", RowVersion = initial.RowVersion }),
                HttpStatusCode.Conflict, "concurrency_conflict");
            Assert.Equal(initial.DisplayName, (await GetAsync(owner, room.Id)).DisplayName);
        }
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("#aabbcc", null)]
    [InlineData("ZZZZZZ", null)]
    [InlineData(null, "  ")]
    public async Task InvalidSettingsAreRejected(string? color, string? displayName)
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        {
            var room = await CreateAsync(owner);
            var initial = await GetAsync(owner, room.Id);
            await AccountTests.AssertProblem(await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}/members/me",
                new UpdateRoomMembershipRequest
                {
                    Color = color, DisplayName = displayName, RowVersion = initial.RowVersion
                }), HttpStatusCode.BadRequest, "validation_failed");
        }
    }

    [Fact]
    public async Task ArchiveAllowsPrivateFavouriteButRejectsAuthorChanges()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        {
            var room = await CreateAsync(owner);
            Assert.Equal(HttpStatusCode.NoContent,
                (await owner.PostAsync($"/api/rooms/{room.Id}/archive", null)).StatusCode);
            var initial = await GetAsync(owner, room.Id);
            var response = await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}/members/me",
                new UpdateRoomMembershipRequest { IsFavourite = true, RowVersion = initial.RowVersion });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var updated = (await response.Content.ReadFromJsonAsync<RoomMembershipResponse>())!;
            await AccountTests.AssertProblem(await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}/members/me",
                new UpdateRoomMembershipRequest { Color = "123456", RowVersion = updated.RowVersion }),
                HttpStatusCode.Conflict, "room_inactive");
        }
    }

    private static async Task<RoomResponse> CreateAsync(HttpClient client)
    {
        var result = await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest { Name = "Membership Test" });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        return (await result.Content.ReadFromJsonAsync<RoomResponse>())!;
    }

    private static async Task JoinAsync(HttpClient client, RoomResponse room)
    {
        var result = await client.PostAsJsonAsync(room.InitialInvitation!.JoinPath + "/join",
            new JoinInvitationRequest { DisplayName = "Guest Name" });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    private static async Task<RoomMembershipResponse> GetAsync(HttpClient client, int roomId) =>
        (await client.GetFromJsonAsync<RoomMembershipResponse>($"/api/rooms/{roomId}/members/me"))!;
}
