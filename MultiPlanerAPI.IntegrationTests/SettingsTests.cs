using System.Net;
using System.Net.Http.Json;
using MultiPlanerSharedModels.Contracts.Auth;
using MultiPlanerSharedModels.Contracts.Users;
using MultiPlanerSharedModels.Contracts.Rooms;
using MultiPlanerSharedModels.Contracts.Events;
using MultiPlanerSharedModels.Contracts.Common;

namespace MultiPlanerAPI.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SettingsTests(ApiFixture fixture)
{
    private static async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == status,
            $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<RoomResponse> Room(HttpClient client)
    {
        return await Read<RoomResponse>(
            await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest { Name = "Settings room" }),
            HttpStatusCode.Created);
    }

    private static async Task<RoomResponse> Join(HttpClient client, RoomResponse room)
    {
        return await Read<RoomResponse>(
            await client.PostAsJsonAsync(room.InitialInvitation!.JoinPath + "/join",
                new JoinInvitationRequest { DisplayName = "Guest" }));
    }

    private static async Task<RoomMemberResponse> Member(HttpClient client, int room, int member)
    {
        return (await client.GetFromJsonAsync<PagedResponse<RoomMemberResponse>>($"/api/rooms/{room}/members"))!.Items
            .Single(m => m.Id == member);
    }

    private static async Task Promote(HttpClient owner, int room, int member)
    {
        var target = await Member(owner, room, member);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsJsonAsync($"/api/rooms/{room}/members/{member}/role",
            new ChangeMemberRoleRequest { IsAdmin = true, RowVersion = target.RowVersion! })).StatusCode);
    }

    private static Task<HttpResponseMessage> Delete(HttpClient client, string url, object body)
    {
        return client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, url) { Content = JsonContent.Create(body) });
    }

    [Fact]
    public async Task NamedThemesArePrivateVersionedAndSavingDoesNotApply()
    {
        var (client, _, _) = await fixture.RegisterAndLoginAsync();
        using var owner = client;
        var (otherClient, _, _) = await fixture.RegisterAndLoginAsync();
        using var other = otherClient;
        var initial = (await owner.GetFromJsonAsync<ThemeResponse>("/api/me/theme"))!;
        Assert.Equal(-1, initial.ThemeId);
        var builtIns = (await owner.GetFromJsonAsync<List<SavedThemeResponse>>("/api/me/themes"))!;
        Assert.Equal(new[] { "Default", "Dark" }, builtIns.Select(t => t.Name));
        var saved = await Read<SavedThemeResponse>(
            await owner.PostAsJsonAsync("/api/me/themes",
                new SaveThemeRequest { Name = "Mój schemat", Palette = new ThemePalette { Accent = "#123456" } }),
            HttpStatusCode.Created);
        Assert.Equal("Mój schemat", saved.Name);
        Assert.Equal("Mój schemat",
            (await owner.GetFromJsonAsync<List<SavedThemeResponse>>("/api/me/themes"))!.Single(t => t.Id == saved.Id)
            .Name);
        var second = await Read<SavedThemeResponse>(
            await owner.PostAsJsonAsync("/api/me/themes",
                new SaveThemeRequest { Name = "Evening", Palette = new ThemePalette { Background = "#111111" } }),
            HttpStatusCode.Created);
        Assert.Equal(-1, (await owner.GetFromJsonAsync<ThemeResponse>("/api/me/theme"))!.ThemeId);
        Assert.Equal(2, (await other.GetFromJsonAsync<List<SavedThemeResponse>>("/api/me/themes"))!.Count);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PutAsJsonAsync("/api/me/theme",
                new ApplyThemeRequest { ThemeId = saved.Id, RowVersion = saved.RowVersion })).StatusCode);
        var active = await Read<ThemeResponse>(await owner.PutAsJsonAsync("/api/me/theme",
            new ApplyThemeRequest { ThemeId = saved.Id, RowVersion = saved.RowVersion }));
        Assert.Equal("#123456", active.Palette.Accent);
        var updated = await Read<SavedThemeResponse>(await owner.PutAsJsonAsync($"/api/me/themes/{saved.Id}",
            new SaveThemeRequest
            {
                Name = "Renamed", Palette = new ThemePalette { Accent = "#654321" }, RowVersion = saved.RowVersion
            }));
        Assert.Equal("#123456", (await owner.GetFromJsonAsync<ThemeResponse>("/api/me/theme"))!.Palette.Accent);
        Assert.Equal(HttpStatusCode.Conflict,
            (await owner.PutAsJsonAsync("/api/me/theme",
                new ApplyThemeRequest
                    { ThemeId = saved.Id, RowVersion = saved.RowVersion, ActiveRowVersion = active.RowVersion }))
            .StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.PutAsJsonAsync("/api/me/themes/-1", new SaveThemeRequest { Name = "Protected" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync("/api/me/themes",
                new SaveThemeRequest { Name = "Invalid", Palette = new ThemePalette { Accent = "red" } })).StatusCode);
        var edits = await Task.WhenAll(
            owner.PutAsJsonAsync($"/api/me/themes/{second.Id}",
                new SaveThemeRequest { Name = "A", RowVersion = second.RowVersion }),
            owner.PutAsJsonAsync($"/api/me/themes/{second.Id}",
                new SaveThemeRequest { Name = "B", RowVersion = second.RowVersion }));
        Assert.Single(edits, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(edits, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Delete(owner, $"/api/me/themes/{saved.Id}",
                new ResetThemeRequest { RowVersion = updated.RowVersion })).StatusCode);
        Assert.Equal(-1, (await owner.GetFromJsonAsync<ThemeResponse>("/api/me/theme"))!.ThemeId);
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync("/api/me/themes", new SaveThemeRequest { Name = "No CSRF" })).StatusCode);
    }

    [Fact]
    public async Task PasswordChangeKeepsCurrentSessionInvalidatesOthersAndChecksOldPassword()
    {
        var (client, user, _) = await fixture.RegisterAndLoginAsync();
        using var owner = client;
        using var second = fixture.Client();
        await ApiFixture.CsrfAsync(second);
        Assert.Equal(HttpStatusCode.OK,
            (await second.PostAsJsonAsync("/api/auth/login",
                new LoginRequest { Email = user.Email!, Password = ApiFixture.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync("/api/me/change-password",
                new ChangePasswordRequest { CurrentPassword = "Wrong9!Password", NewPassword = "Another9!Password" }))
            .StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.PostAsJsonAsync("/api/me/change-password",
                new ChangePasswordRequest { CurrentPassword = ApiFixture.Password, NewPassword = "Another9!Password" }))
            .StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/me")).StatusCode);
        await ApiFixture.CsrfAsync(second);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await second.PostAsJsonAsync("/api/auth/login",
                new LoginRequest { Email = user.Email!, Password = ApiFixture.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await second.PostAsJsonAsync("/api/auth/login",
                new LoginRequest { Email = user.Email!, Password = "Another9!Password" })).StatusCode);
    }

    [Fact]
    public async Task AdminManagesOrdinaryMembersButCannotManageAdminsOrOwner()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using var o = owner;
        var (admin, _, _) = await fixture.RegisterAndLoginAsync();
        using var a = admin;
        var (member, _, _) = await fixture.RegisterAndLoginAsync();
        using var m = member;
        var room = await Room(owner);
        var adminJoin = await Join(admin, room);
        var memberJoin = await Join(member, room);
        await Promote(owner, room.Id, adminJoin.MemberId);
        await Promote(admin, room.Id, memberJoin.MemberId);
        var target = await Member(admin, room.Id, memberJoin.MemberId);
        Assert.True(target.IsAdmin);
        Assert.False(target.CanRemove);
        Assert.False(target.CanDemote);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Delete(admin, $"/api/rooms/{room.Id}/members/{target.Id}",
                new RemoveMemberRequest { RowVersion = target.RowVersion! })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PatchAsJsonAsync($"/api/rooms/{room.Id}/members/{target.Id}/role",
                new ChangeMemberRoleRequest { RowVersion = target.RowVersion! })).StatusCode);
        var ownerMember = await Member(admin, room.Id, room.MemberId);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Delete(admin, $"/api/rooms/{room.Id}/members/{room.MemberId}",
                new RemoveMemberRequest { RowVersion = ownerMember.RowVersion! })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Delete(owner, $"/api/rooms/{room.Id}/members/{target.Id}",
                new RemoveMemberRequest { RowVersion = target.RowVersion! })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/rooms/{room.Id}")).StatusCode);
        var rejoined = await Join(member, room);
        Assert.Equal(target.Id, rejoined.MemberId);
        Assert.False(rejoined.IsAdmin);
        var fresh = await Member(admin, room.Id, target.Id);
        Assert.Equal(HttpStatusCode.Conflict,
            (await Delete(admin, $"/api/rooms/{room.Id}/members/{target.Id}",
                new RemoveMemberRequest { RowVersion = target.RowVersion! })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Delete(admin, $"/api/rooms/{room.Id}/members/{target.Id}",
                new RemoveMemberRequest { RowVersion = fresh.RowVersion! })).StatusCode);
    }

    [Fact]
    public async Task AdminCanManageSettingsButNotOwnerFieldsAndGuestCannotBecomeAdmin()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using var o = owner;
        var (admin, _, _) = await fixture.RegisterAndLoginAsync();
        using var a = admin;
        var room = await Room(owner);
        var joined = await Join(admin, room);
        await Promote(owner, room.Id, joined.MemberId);
        var updated = await Read<RoomResponse>(await admin.PatchAsJsonAsync($"/api/rooms/{room.Id}",
            new UpdateRoomRequest { Name = "Admin renamed", RowVersion = room.RowVersion! }));
        Assert.Equal("Admin renamed", updated.Name);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PatchAsJsonAsync($"/api/rooms/{room.Id}",
                new UpdateRoomRequest { TimeZoneId = "UTC", RowVersion = updated.RowVersion! })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PostAsync($"/api/rooms/{room.Id}/archive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync($"/api/rooms/{room.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created,
            (await admin.PostAsJsonAsync($"/api/rooms/{room.Id}/invitations", new CreateRoomInvitationRequest()))
            .StatusCode);
        var invitation =
            (await admin.GetFromJsonAsync<RoomInvitationResponse>($"/api/rooms/{room.Id}/invitations/current"))!;
        using var guest = fixture.Client();
        await ApiFixture.CsrfAsync(guest);
        var guestRoom = await Read<RoomResponse>(await guest.PostAsJsonAsync(invitation.JoinPath + "/join",
            new JoinInvitationRequest { DisplayName = "Guest" }));
        await ApiFixture.CsrfAsync(guest);
        var guestMember = await Member(admin, room.Id, guestRoom.MemberId);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PatchAsJsonAsync($"/api/rooms/{room.Id}/members/{guestMember.Id}/role",
                new ChangeMemberRoleRequest { IsAdmin = true, RowVersion = guestMember.RowVersion! })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync("/api/me/theme")).StatusCode);
        var freshAdmin = await Member(owner, room.Id, joined.MemberId);
        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}/members/{joined.MemberId}/role",
                new ChangeMemberRoleRequest { RowVersion = freshAdmin.RowVersion! })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PostAsJsonAsync($"/api/rooms/{room.Id}/event-types",
                new CreateEventTypeRequest { Name = "No longer allowed" })).StatusCode);
    }

    [Fact]
    public async Task EventTypesAreRoomScopedVersionedAndArchivePreservesExistingEvents()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using var o = owner;
        var room = await Room(owner);
        var other = await Room(owner);
        var defaults = (await owner.GetFromJsonAsync<List<EventTypeResponse>>($"/api/rooms/{room.Id}/event-types"))!;
        Assert.Equal(6, defaults.Count);
        var type = await Read<EventTypeResponse>(
            await owner.PostAsJsonAsync($"/api/rooms/{room.Id}/event-types",
                new CreateEventTypeRequest { Name = "Custom", Color = "AABBCC" }), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.Conflict,
            (await owner.PostAsJsonAsync($"/api/rooms/{room.Id}/event-types",
                new CreateEventTypeRequest { Name = " custom " })).StatusCode);
        var request = new CreateEventRequest
        {
            Title = "Typed", EventTypeId = type.Id,
            Time = new EventTimeRequest
                { StartsAtUtc = DateTimeOffset.UtcNow.AddHours(1), EndsAtUtc = DateTimeOffset.UtcNow.AddHours(2) }
        };
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync($"/api/rooms/{other.Id}/events", request)).StatusCode);
        var ev = await Read<EventResponse>(await owner.PostAsJsonAsync($"/api/rooms/{room.Id}/events", request),
            HttpStatusCode.Created);
        Assert.Equal("AABBCC", ev.Color);
        var edited = await Read<EventTypeResponse>(await owner.PatchAsJsonAsync(
            $"/api/rooms/{room.Id}/event-types/{type.Id}",
            new UpdateEventTypeRequest { Color = "112233", RowVersion = type.RowVersion }));
        Assert.Equal("112233",
            (await owner.GetFromJsonAsync<EventResponse>($"/api/rooms/{room.Id}/events/{ev.Id}"))!.Color);
        var custom = await Read<EventResponse>(await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}/events/{ev.Id}",
            new UpdateEventRequest { Color = "FEDCBA", RowVersion = ev.RowVersion }));
        await Read<EventTypeResponse>(await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}/event-types/{type.Id}",
            new UpdateEventTypeRequest { IsArchived = true, RowVersion = edited.RowVersion }));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync($"/api/rooms/{room.Id}/events", request)).StatusCode);
        var preserved = await Read<EventResponse>(await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}/events/{ev.Id}",
            new UpdateEventRequest { Title = "Still editable", RowVersion = custom.RowVersion }));
        Assert.True(preserved.EventTypeIsArchived);
        Assert.Equal("FEDCBA", preserved.Color);
        Assert.Equal(HttpStatusCode.Conflict,
            (await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}/event-types/{type.Id}",
                new UpdateEventTypeRequest { Name = "Stale", RowVersion = type.RowVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/rooms/{room.Id}")).StatusCode);
    }

    [Fact]
    public async Task AvatarUploadRequiresImageAndSharedMembershipAndCanBeRemoved()
    {
        var (owner, user, _) = await fixture.RegisterAndLoginAsync();
        using var o = owner;
        var (member, _, _) = await fixture.RegisterAndLoginAsync();
        using var m = member;
        var room = await Room(owner);
        using var invalid = new MultipartFormDataContent();
        invalid.Add(new ByteArrayContent("not an image at all"u8.ToArray()), "file", "fake.png");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsync("/api/me/avatar", invalid)).StatusCode);
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(png), "file", "avatar.png");
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsync("/api/me/avatar", form)).StatusCode);
        var url = (await owner.GetFromJsonAsync<SessionResponse>("/api/me"))!.AvatarUrl!;
        Assert.Equal(png, await owner.GetByteArrayAsync(url));
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(url)).StatusCode);
        var joined = await Join(member, room);
        Assert.Equal(png, await member.GetByteArrayAsync(url));
        var listed = await Member(member, room.Id, room.MemberId);
        Assert.Equal(url, listed.AvatarUrl);
        var nickBefore = (await Member(owner, room.Id, room.MemberId)).DisplayName;
        await Read<SessionResponse>(await owner.PatchAsJsonAsync("/api/me",
            new UpdateProfileRequest { DisplayName = "Changed global nick" }));
        Assert.Equal(nickBefore, (await Member(owner, room.Id, room.MemberId)).DisplayName);
        var target = await Member(owner, room.Id, joined.MemberId);
        Assert.Equal(HttpStatusCode.NoContent,
            (await Delete(owner, $"/api/rooms/{room.Id}/members/{target.Id}",
                new RemoveMemberRequest { RowVersion = target.RowVersion! })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/me/avatar")).StatusCode);
        Assert.Null((await owner.GetFromJsonAsync<SessionResponse>("/api/me"))!.AvatarUrl);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(url)).StatusCode);
    }
}