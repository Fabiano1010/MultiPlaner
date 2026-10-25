using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Models;
using MultiPlanerSharedModels.Contracts.Common;
using MultiPlanerSharedModels.Contracts.Events;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class StageFourTests(ApiFixture fixture)
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
    private const string Range = "from=2026-10-01T00:00:00Z&to=2026-12-01T00:00:00Z";
    private static string Events(int room) => $"/api/rooms/{room}/events";
    private static CreateEventRequest Request(string title = "Planning") => new()
    {
        Title = title, Time = new() { StartsAtUtc = Start, EndsAtUtc = Start.AddHours(1) }, IsHighPriority = true
    };
    private static async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == expected, $"Expected {expected}; got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static async Task<RoomResponse> Room(HttpClient client) =>
        await Read<RoomResponse>(await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest
            { Name = "Stage Four", TimeZoneId = "Europe/Warsaw" }), HttpStatusCode.Created);
    private static async Task<RoomResponse> Join(HttpClient client, RoomResponse room) =>
        await Read<RoomResponse>(await client.PostAsJsonAsync(room.InitialInvitation!.JoinPath + "/join",
            new JoinInvitationRequest { DisplayName = "Calendar Guest" }));
    private static async Task<EventResponse> Create(HttpClient client, int room, CreateEventRequest? request = null) =>
        await Read<EventResponse>(await client.PostAsJsonAsync(Events(room), request ?? Request()), HttpStatusCode.Created);
    private static Task<HttpResponseMessage> Delete(HttpClient client, int room, EventResponse ev) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{Events(room)}/{ev.Id}")
            { Content = JsonContent.Create(new EventVersionRequest { RowVersion = ev.RowVersion }) });

    [Fact]
    public async Task GuestEventsHaveServerAuthorAndIndependentPriorityAndPinWithScopedPermissions()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        var (outsider, _, _) = await fixture.RegisterAndLoginAsync();
        using var ownerClient = owner;
        using var outsiderClient = outsider;
        using var guest = fixture.Client();
        var room = await Room(owner);
        await ApiFixture.CsrfAsync(guest);
        var joined = await Join(guest, room);
        await ApiFixture.CsrfAsync(guest);
        var ev = await Create(guest, room.Id);
        Assert.Equal(joined.MemberId, ev.Author.MemberId);
        Assert.True(ev.Author.IsGuest);
        Assert.False(ev.Author.IsOwner);
        Assert.True(ev.Permissions.CanEdit);
        Assert.Equal(joined.Membership!.Color.ToUpperInvariant(), ev.Color);
        Assert.Null(ev.CustomColor);
        Assert.True(ev.IsHighPriority);
        Assert.False(ev.IsPinned);
        Assert.Equal(new[] { joined.MemberId }, ev.ParticipantMemberIds);
        var ownerView = await Read<EventResponse>(await owner.GetAsync($"{Events(room.Id)}/{ev.Id}"));
        Assert.False(ownerView.Permissions.CanEdit);
        Assert.True(ownerView.Permissions.CanPin);
        await AccountTests.AssertProblem(await owner.PatchAsJsonAsync($"{Events(room.Id)}/{ev.Id}",
            new UpdateEventRequest { Title = "Not mine", RowVersion = ev.RowVersion }), HttpStatusCode.Forbidden, "entry_author_required");
        var pinned = await Read<EventResponse>(await owner.PutAsJsonAsync($"{Events(room.Id)}/{ev.Id}/pin",
            new PinEventRequest { IsPinned = true, RowVersion = ev.RowVersion }));
        Assert.True(pinned.IsHighPriority && pinned.IsPinned);
        await AccountTests.AssertProblem(await outsider.GetAsync($"{Events(room.Id)}/{ev.Id}"), HttpStatusCode.NotFound, "room_not_found");
        var otherRoom = await Room(owner);
        await AccountTests.AssertProblem(await owner.GetAsync($"{Events(otherRoom.Id)}/{ev.Id}"), HttpStatusCode.NotFound, "event_not_found");
        await AccountTests.AssertProblem(await guest.PostAsJsonAsync(Events(room.Id), new
        {
            title = "Forged", authorMemberId = room.MemberId, time = new { startsAtUtc = Start, endsAtUtc = Start.AddHours(1) }
        }), HttpStatusCode.BadRequest, "validation_failed");
        guest.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        await AccountTests.AssertProblem(await guest.PostAsJsonAsync(Events(room.Id), Request()), HttpStatusCode.BadRequest, "invalid_csrf");
        using var anonymous = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Events(room.Id) + "?" + Range)).StatusCode);
    }

    [Fact]
    public async Task UpdatesAreVersionedIncludingParticipantsAndDeleteRemovesDependencies()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using var client = owner;
        var room = await Room(owner);
        var otherRoom = await Room(owner);
        var ev = await Create(owner, room.Id);
        var updates = await Task.WhenAll(
            owner.PatchAsJsonAsync($"{Events(room.Id)}/{ev.Id}", new UpdateEventRequest { Title = "First", RowVersion = ev.RowVersion }),
            owner.PatchAsJsonAsync($"{Events(room.Id)}/{ev.Id}", new UpdateEventRequest { Title = "Second", RowVersion = ev.RowVersion }));
        var winner = await Read<EventResponse>(Assert.Single(updates, r => r.StatusCode == HttpStatusCode.OK));
        await AccountTests.AssertProblem(Assert.Single(updates, r => r.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "concurrency_conflict");
        await AccountTests.AssertProblem(await Delete(owner, room.Id, ev), HttpStatusCode.Conflict, "concurrency_conflict");
        await AccountTests.AssertProblem(await owner.PatchAsJsonAsync($"{Events(room.Id)}/{ev.Id}", new UpdateEventRequest
            { ParticipantMemberIds = [otherRoom.MemberId], RowVersion = winner.RowVersion }), HttpStatusCode.BadRequest, "validation_failed");
        var changed = await Read<EventResponse>(await owner.PatchAsJsonAsync($"{Events(room.Id)}/{ev.Id}", new UpdateEventRequest
            { ParticipantMemberIds = [], Color = "aabbcc", Description = "Notes", IsHighPriority = false, RowVersion = winner.RowVersion }));
        Assert.Empty(changed.ParticipantMemberIds);
        Assert.NotEqual(winner.RowVersion, changed.RowVersion);
        Assert.Equal("AABBCC", changed.Color);
        var cleared = await Read<EventResponse>(await owner.PatchAsJsonAsync($"{Events(room.Id)}/{ev.Id}", new UpdateEventRequest
            { Color = "", Description = "", ParticipantMemberIds = [room.MemberId], RowVersion = changed.RowVersion }));
        Assert.Null(cleared.CustomColor);
        Assert.Null(cleared.Description);
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(owner, room.Id, cleared)).StatusCode);
        await AccountTests.AssertProblem(await owner.GetAsync($"{Events(room.Id)}/{ev.Id}"), HttpStatusCode.NotFound, "event_not_found");
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.EventParticipants.AnyAsync(p => p.EventId == ev.Id));
    }

    [Fact]
    public async Task RangeQueriesIncludeOverlapsAndExcludeTouchingBoundariesWithStablePaging()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using var client = owner;
        var room = await Room(owner);
        var overlap = await Create(owner, room.Id, new() { Title = "Across months", Category = EventCategory.Training,
            Time = new() { StartsAtUtc = Start.AddDays(-3), EndsAtUtc = Start.AddHours(1) } });
        var sameStart = await Create(owner, room.Id);
        var second = await Create(owner, room.Id);
        await Create(owner, room.Id, new() { Title = "Touches start", Time = new() { StartsAtUtc = Start.AddHours(-1), EndsAtUtc = Start } });
        await Create(owner, room.Id, new() { Title = "Touches end", Time = new() { StartsAtUtc = Start.AddHours(1), EndsAtUtc = Start.AddHours(2) } });
        const string hour = "from=2026-11-01T10:00:00Z&to=2026-11-01T11:00:00Z";
        var page = await Read<PagedResponse<EventResponse>>(await owner.GetAsync(Events(room.Id) + "?" + hour + "&pageSize=2"));
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new[] { overlap.Id, sameStart.Id }, page.Items.Select(x => x.Id));
        var next = await Read<PagedResponse<EventResponse>>(await owner.GetAsync(Events(room.Id) + "?" + hour + "&pageSize=2&page=2"));
        Assert.Equal(second.Id, Assert.Single(next.Items).Id);
        var training = await Read<PagedResponse<EventResponse>>(await owner.GetAsync(Events(room.Id) + "?" + hour + "&category=Training&isHighPriority=false&isPinned=false&authorMemberId=" + room.MemberId));
        Assert.Equal(overlap.Id, Assert.Single(training.Items).Id);
        foreach (var query in new[] { "", "?from=2026-11-01&to=2026-10-01", "?" + hour + "&category=Unknown", "?" + hour + "&status=unknown" })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync(Events(room.Id) + query)).StatusCode);
    }

    [Fact]
    public async Task AllDayDatesSurviveRoomZoneChangeAndArchiveBlocksAllWrites()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using var client = owner;
        var room = await Room(owner);
        var ev = await Create(owner, room.Id, new() { Title = "DST day", Time = new()
            { IsAllDay = true, StartDate = new(2026, 10, 25), EndDateExclusive = new(2026, 10, 26) } });
        Assert.Equal(25, (ev.EndsAtUtc - ev.StartsAtUtc).TotalHours);
        await Read<RoomResponse>(await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}", new UpdateRoomRequest
            { TimeZoneId = "America/New_York", RowVersion = room.RowVersion! }));
        var read = await Read<EventResponse>(await owner.GetAsync($"{Events(room.Id)}/{ev.Id}"));
        Assert.Equal("Europe/Warsaw", read.TimeZoneId);
        Assert.Equal(ev.StartsAtUtc, read.StartsAtUtc);
        Assert.Equal("America/New_York", read.RoomTimeZoneId);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/rooms/{room.Id}/archive", null)).StatusCode);
        var archived = await Read<PagedResponse<EventResponse>>(await owner.GetAsync(Events(room.Id) + "?" + Range));
        Assert.False(Assert.Single(archived.Items).Permissions.CanEdit);
        Assert.False(archived.Items[0].Permissions.CanPin);
        foreach (var response in new[]
        {
            await owner.PostAsJsonAsync(Events(room.Id), Request()),
            await owner.PatchAsJsonAsync($"{Events(room.Id)}/{ev.Id}", new UpdateEventRequest { Title = "Changed", RowVersion = ev.RowVersion }),
            await owner.PutAsJsonAsync($"{Events(room.Id)}/{ev.Id}/pin", new PinEventRequest { IsPinned = true, RowVersion = ev.RowVersion }),
            await Delete(owner, room.Id, ev)
        }) await AccountTests.AssertProblem(response, HttpStatusCode.Conflict, "room_inactive");
        var globalActive = await Read<PagedResponse<EventResponse>>(await owner.GetAsync("/api/me/events?" + Range));
        Assert.Empty(globalActive.Items);
        var globalArchive = await Read<PagedResponse<EventResponse>>(await owner.GetAsync("/api/me/events?" + Range + "&status=archived"));
        Assert.Equal(ev.Id, Assert.Single(globalArchive.Items).Id);
    }

    [Fact]
    public async Task LeavingPreservesHistoryRevokesEveryAccessAndRejoinRestoresOriginalMembership()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        var (member, _, _) = await fixture.RegisterAndLoginAsync();
        using var ownerClient = owner;
        using var memberClient = member;
        var room = await Room(owner);
        var joined = await Join(member, room);
        var ev = await Create(member, room.Id);
        var folder = await Read<UserFolderResponse>(await member.PostAsJsonAsync("/api/me/folders", new CreateUserFolderRequest { Name = "Work" }), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.NoContent, (await member.PutAsync($"/api/me/folders/{folder.Id}/rooms/{room.Id}", null)).StatusCode);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ChatMessages.Add(new() { RoomId = room.Id, AuthorMemberId = joined.MemberId, ClientRequestId = Guid.NewGuid(), Text = "Keep history", SentAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        await AccountTests.AssertProblem(await owner.DeleteAsync($"/api/rooms/{room.Id}/members/me"), HttpStatusCode.Conflict, "owner_cannot_leave");
        Assert.Equal(HttpStatusCode.NoContent, (await member.DeleteAsync($"/api/rooms/{room.Id}/members/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await member.DeleteAsync($"/api/rooms/{room.Id}/members/me")).StatusCode);
        foreach (var path in new[] { $"/api/rooms/{room.Id}", $"/api/rooms/{room.Id}/cover", $"/api/rooms/{room.Id}/members", $"{Events(room.Id)}/{ev.Id}" })
            await AccountTests.AssertProblem(await member.GetAsync(path), HttpStatusCode.NotFound, "room_not_found");
        Assert.Empty((await Read<UserFolderResponse>(await member.GetAsync($"/api/me/folders/{folder.Id}"))).RoomIds);
        Assert.Empty(await member.GetFromJsonAsync<RoomResponse[]>("/api/rooms?status=all") ?? []);
        Assert.Empty((await Read<PagedResponse<EventResponse>>(await member.GetAsync("/api/me/events?" + Range + "&status=all"))).Items);
        await AccountTests.AssertProblem(await member.PutAsync($"/api/me/folders/{folder.Id}/rooms/{room.Id}", null), HttpStatusCode.NotFound, "room_not_found");
        await AccountTests.AssertProblem(await owner.PostAsJsonAsync(Events(room.Id), new CreateEventRequest
            { Title = "Invalid participant", Time = Request().Time, ParticipantMemberIds = [joined.MemberId] }), HttpStatusCode.BadRequest, "validation_failed");
        var preserved = await Read<EventResponse>(await owner.GetAsync($"{Events(room.Id)}/{ev.Id}"));
        Assert.True(preserved.Author.HasLeft);
        var members = await Read<PagedResponse<RoomMemberResponse>>(await owner.GetAsync($"/api/rooms/{room.Id}/members"));
        Assert.DoesNotContain(members.Items, x => x.Id == joined.MemberId);
        var invitation = await Read<RoomInvitationResponse>(await owner.PostAsJsonAsync($"/api/rooms/{room.Id}/invitations", new CreateRoomInvitationRequest { MaxUses = 1 }), HttpStatusCode.Created);
        var rejoined = await Read<RoomResponse>(await member.PostAsJsonAsync(invitation.JoinPath + "/join", new JoinInvitationRequest()));
        Assert.Equal(joined.MemberId, rejoined.MemberId);
        Assert.NotNull(rejoined.OwnerDisplayName);
        var restored = await Read<EventResponse>(await member.GetAsync($"{Events(room.Id)}/{ev.Id}"));
        Assert.True(restored.Permissions.CanEdit);
        Assert.False(restored.Author.HasLeft);
        await using var finalScope = fixture.Factory.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await finalDb.ChatMessages.AnyAsync(m => m.AuthorMemberId == joined.MemberId && m.Text == "Keep history"));
        Assert.Equal(1, await finalDb.RoomInvitations.Where(i => i.Id == invitation.Id).Select(i => i.UseCount).SingleAsync());
    }

    [Fact]
    public async Task GuestLeaveRacingWithWriteCannotCreateAfterLeaveAndCanRejoinWithSameSession()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using var ownerClient = owner;
        using var guest = fixture.Client();
        var room = await Room(owner);
        await ApiFixture.CsrfAsync(guest);
        var joined = await Join(guest, room);
        await ApiFixture.CsrfAsync(guest);
        var responses = await Task.WhenAll(guest.PostAsJsonAsync(Events(room.Id), Request()),
            guest.DeleteAsync($"/api/rooms/{room.Id}/members/me"));
        Assert.Equal(HttpStatusCode.NoContent, responses[1].StatusCode);
        Assert.Contains(responses[0].StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.NotFound });
        await AccountTests.AssertProblem(await guest.PostAsJsonAsync(Events(room.Id), Request()), HttpStatusCode.NotFound, "room_not_found");
        var rejoined = await Join(guest, room);
        Assert.Equal(joined.MemberId, rejoined.MemberId);
        Assert.NotNull(rejoined.OwnerDisplayName);
    }

    [Fact]
    public async Task GlobalListIncludesOnlyAccessibleRoomsAndHonorsRoomFilterAndExpiry()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        var (other, _, _) = await fixture.RegisterAndLoginAsync();
        using var ownerClient = owner;
        using var otherClient = other;
        var mine = await Room(owner);
        var shared = await Room(other);
        var secret = await Room(other);
        var myEvent = await Create(owner, mine.Id);
        var sharedEvent = await Create(other, shared.Id);
        await Create(other, secret.Id);
        await Join(owner, shared);
        var all = await Read<PagedResponse<EventResponse>>(await owner.GetAsync("/api/me/events?" + Range));
        Assert.Equal(new[] { myEvent.Id, sharedEvent.Id }, all.Items.Select(x => x.Id));
        var selected = await Read<PagedResponse<EventResponse>>(await owner.GetAsync("/api/me/events?" + Range + "&roomId=" + shared.Id));
        Assert.Equal(sharedEvent.Id, Assert.Single(selected.Items).Id);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            await db.Rooms.Where(r => r.Id == shared.Id).ExecuteUpdateAsync(u => u
                .SetProperty(r => r.CreatedAtUtc, now.AddDays(-2)).SetProperty(r => r.ExpiresAtUtc, now.AddDays(-1)));
        }
        var expired = await Read<PagedResponse<EventResponse>>(await owner.GetAsync("/api/me/events?" + Range + "&status=all"));
        Assert.Equal(myEvent.Id, Assert.Single(expired.Items).Id);
        await AccountTests.AssertProblem(await owner.GetAsync(Events(shared.Id) + "?" + Range), HttpStatusCode.Gone, "room_expired");
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Rooms.Where(r => r.Id == shared.Id).ExecuteUpdateAsync(u => u.SetProperty(r => r.ArchiveOnExpiry, true));
        }
        var autoArchived = await Read<PagedResponse<EventResponse>>(await owner.GetAsync("/api/me/events?" + Range + "&status=archived"));
        var historical = Assert.Single(autoArchived.Items);
        Assert.Equal(sharedEvent.Id, historical.Id);
        Assert.True(historical.RoomIsArchived);
        Assert.False(historical.Permissions.CanPin);
    }

    [Fact]
    public async Task InvalidPayloadsAndMissingVersionsCannotMutateEvents()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using var client = owner;
        var room = await Room(owner);
        foreach (var request in new CreateEventRequest[]
        {
            new() { Title = "  ", Time = Request().Time },
            new() { Title = "Missing time" },
            new() { Title = "Wrong interval", Time = new() { StartsAtUtc = Start, EndsAtUtc = Start } },
            new() { Title = "Duplicate participants", Time = Request().Time, ParticipantMemberIds = [room.MemberId, room.MemberId] },
            new() { Title = "Wrong color", Time = Request().Time, Color = "#AABBCC" }
        }) await AccountTests.AssertProblem(await owner.PostAsJsonAsync(Events(room.Id), request), HttpStatusCode.BadRequest, "validation_failed");
        var ev = await Create(owner, room.Id);
        await AccountTests.AssertProblem(await owner.PatchAsJsonAsync($"{Events(room.Id)}/{ev.Id}", new { title = "No version" }), HttpStatusCode.BadRequest, "validation_failed");
        await AccountTests.AssertProblem(await owner.PutAsJsonAsync($"{Events(room.Id)}/{ev.Id}/pin", new { isPinned = true }), HttpStatusCode.BadRequest, "validation_failed");
        await AccountTests.AssertProblem(await owner.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{Events(room.Id)}/{ev.Id}")
            { Content = JsonContent.Create(new { }) }), HttpStatusCode.BadRequest, "validation_failed");
        var read = await Read<EventResponse>(await owner.GetAsync($"{Events(room.Id)}/{ev.Id}"));
        Assert.Equal(ev.RowVersion, read.RowVersion);
        Assert.Equal(ev.Title, read.Title);
    }
}
