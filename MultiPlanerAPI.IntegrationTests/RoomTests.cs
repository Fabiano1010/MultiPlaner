using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MultiPlanerAPI.Data;
using MultiPlanerSharedModels.Contracts.Auth;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class RoomTests(ApiFixture fixture)
{
    [Fact]
    public async Task OwnerCanCreateAndArchiveRoomAndOthersCannotManageIt()
    {
        var (owner, ownerUser, _) = await fixture.RegisterAndLoginAsync();
        var (other, _, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        using (other)
        {
            var room = await CreateRoomAsync(owner);
            Assert.Equal(ownerUser.UserId, room.OwnerUserId);
            Assert.True(room.IsOwner);
            Assert.True(room.MemberId > 0);
            Assert.NotNull(room.InitialInvitation);
            Assert.StartsWith("https://client.example/join/", room.InitialInvitation.JoinUrl);
            Assert.Contains((await owner.GetFromJsonAsync<RoomResponse[]>("/api/rooms"))!, r => r.Id == room.Id);
            Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/rooms/{room.Id}")).StatusCode);
            await AccountTests.AssertProblem(
                await other.PostAsJsonAsync($"/api/rooms/{room.Id}/invitations", new CreateRoomInvitationRequest()),
                HttpStatusCode.Forbidden, "room_owner_required");
            await AccountTests.AssertProblem(await other.DeleteAsync($"/api/rooms/{room.Id}"),
                HttpStatusCode.Forbidden, "room_owner_required");

            var invitation = await CreateInvitationAsync(owner, room.Id);
            Assert.Equal(HttpStatusCode.NoContent,
                (await owner.PostAsync($"/api/rooms/{room.Id}/archive", null)).StatusCode);
            Assert.DoesNotContain((await owner.GetFromJsonAsync<RoomResponse[]>("/api/rooms"))!, r => r.Id == room.Id);
            Assert.Contains((await owner.GetFromJsonAsync<RoomResponse[]>("/api/rooms?status=archived"))!,
                r => r.Id == room.Id && r.ArchivedAtUtc is not null);
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/rooms/{room.Id}")).StatusCode);
            await AccountTests.AssertProblem(await owner.GetAsync(invitation.JoinPath),
                HttpStatusCode.Gone, "invitation_unavailable");
        }
    }

    [Fact]
    public async Task OwnerCanEditRoomAndMembersAreScopedToTheRoom()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        var (other, _, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        using (other)
        using (var guest = fixture.Client())
        {
            var room = await CreateRoomAsync(owner);
            var expiry = DateTimeOffset.UtcNow.AddMonths(4);
            var patch = await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}", new UpdateRoomRequest
            {
                Name = "  Updated Room  ", TimeZoneId = "UTC", ExpiresAtUtc = expiry
            });
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
            var updated = (await patch.Content.ReadFromJsonAsync<RoomResponse>())!;
            Assert.Equal("Updated Room", updated.Name);
            Assert.Equal("UTC", updated.TimeZoneId);
            Assert.Equal(expiry, updated.ExpiresAtUtc);
            await AccountTests.AssertProblem(
                await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}", new UpdateRoomRequest { Name = " " }),
                HttpStatusCode.BadRequest, "validation_failed");
            await AccountTests.AssertProblem(
                await other.PatchAsJsonAsync($"/api/rooms/{room.Id}", new UpdateRoomRequest { Name = "Wrong" }),
                HttpStatusCode.Forbidden, "room_owner_required");
            Assert.Equal(HttpStatusCode.NotFound,
                (await other.GetAsync($"/api/rooms/{room.Id}/members")).StatusCode);

            var invitation = await CreateInvitationAsync(owner, room.Id);
            await ApiFixture.CsrfAsync(guest);
            Assert.Equal(HttpStatusCode.OK,
                (await guest.PostAsJsonAsync("/api/invitations/join",
                    new JoinByTokenRequest { Token = TokenOf(invitation), DisplayName = "Guest One" })).StatusCode);
            var members = await guest.GetFromJsonAsync<MultiPlanerSharedModels.Contracts.Common.PagedResponse<RoomMemberResponse>>(
                $"/api/rooms/{room.Id}/members?page=1&pageSize=1");
            Assert.Equal(2, members!.TotalCount);
            Assert.Single(members.Items);
            Assert.True(members.Items[0].IsOwner);
            var secondPage = await guest.GetFromJsonAsync<MultiPlanerSharedModels.Contracts.Common.PagedResponse<RoomMemberResponse>>(
                $"/api/rooms/{room.Id}/members?page=2&pageSize=1");
            Assert.True(secondPage!.Items[0].IsGuest);
        }
    }

    [Fact]
    public async Task DeleteRemovesRoomAndDependentRowsWhileKeepingAccounts()
    {
        var (owner, ownerUser, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        {
            var room = await CreateRoomAsync(owner);
            var eventId = 0;
            await using (var scope = fixture.Factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = DateTimeOffset.UtcNow;
                var entry = new MultiPlanerAPI.Models.CalendarEvent
                {
                    RoomId = room.Id, AuthorMemberId = room.MemberId,
                    Title = "Meeting", StartsAtUtc = now.AddHours(1), EndsAtUtc = now.AddHours(2)
                };
                db.CalendarEvents.Add(entry);
                await db.SaveChangesAsync();
                eventId = entry.Id;
                db.EventParticipants.Add(new MultiPlanerAPI.Models.EventParticipant
                    { RoomId = room.Id, EventId = entry.Id, MemberId = room.MemberId });
                db.EventAttachments.Add(new MultiPlanerAPI.Models.EventAttachment
                {
                    EventId = entry.Id, StorageKey = Guid.NewGuid().ToString("N"),
                    OriginalFileName = "image.png", ContentType = "image/png", SizeBytes = 16
                });
                db.ChatMessages.Add(new MultiPlanerAPI.Models.ChatMessage
                {
                    RoomId = room.Id, AuthorMemberId = room.MemberId,
                    ClientRequestId = Guid.NewGuid(), Text = "Hello", SentAtUtc = now
                });
                db.AvailabilityIntervals.Add(new MultiPlanerAPI.Models.AvailabilityInterval
                {
                    RoomId = room.Id, MemberId = room.MemberId,
                    StartsAtUtc = now.AddHours(1), EndsAtUtc = now.AddHours(2)
                });
                db.RoomActivities.Add(new MultiPlanerAPI.Models.RoomActivity
                {
                    RoomId = room.Id, ActorMemberId = room.MemberId,
                    Kind = "Test", ResourceId = entry.Id.ToString(), OccurredAtUtc = now
                });
                db.OutboxMessages.Add(new MultiPlanerAPI.Models.OutboxMessage
                {
                    RoomId = room.Id, Kind = "Test", PayloadJson = "{}", CreatedAtUtc = now
                });
                await db.SaveChangesAsync();
            }

            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/rooms/{room.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/rooms/{room.Id}")).StatusCode);
            await using var finalScope = fixture.Factory.Services.CreateAsyncScope();
            var finalDb = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await finalDb.Rooms.AnyAsync(r => r.Id == room.Id));
            Assert.False(await finalDb.RoomMembers.AnyAsync(m => m.RoomId == room.Id));
            Assert.False(await finalDb.RoomInvitations.AnyAsync(i => i.RoomId == room.Id));
            Assert.False(await finalDb.CalendarEvents.AnyAsync(e => e.RoomId == room.Id));
            Assert.False(await finalDb.EventParticipants.AnyAsync(p => p.RoomId == room.Id));
            Assert.False(await finalDb.EventAttachments.AnyAsync(a => a.EventId == eventId));
            Assert.False(await finalDb.ChatMessages.AnyAsync(m => m.RoomId == room.Id));
            Assert.False(await finalDb.AvailabilityIntervals.AnyAsync(a => a.RoomId == room.Id));
            Assert.False(await finalDb.RoomActivities.AnyAsync(a => a.RoomId == room.Id));
            Assert.False(await finalDb.OutboxMessages.AnyAsync(m => m.RoomId == room.Id));
            Assert.True(await finalDb.Users.AnyAsync(u => u.Id == ownerUser.UserId));
        }
    }

    [Fact]
    public async Task SingleUseInvitationAdmitsOneGuestAndCreatesMembershipAndSession()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        using (var guest = fixture.Client())
        using (var secondGuest = fixture.Client())
        {
            var room = await CreateRoomAsync(owner);
            var invitation = await CreateInvitationAsync(owner, room.Id, maxUses: 1);
            await ApiFixture.CsrfAsync(guest);
            var previewResult = await guest.PostAsJsonAsync("/api/invitations/preview",
                new PreviewInvitationRequest { Token = TokenOf(invitation) });
            Assert.Equal(HttpStatusCode.OK, previewResult.StatusCode);
            var preview = await previewResult.Content.ReadFromJsonAsync<InvitationPreviewResponse>();
            Assert.Equal(room.Id, preview!.RoomId);
            Assert.Equal(room.Name, preview.RoomName);

            var joined = await guest.PostAsJsonAsync("/api/invitations/join",
                new JoinByTokenRequest { Token = TokenOf(invitation), DisplayName = "Guest One" });
            Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
            var membership = await joined.Content.ReadFromJsonAsync<RoomResponse>();
            Assert.Equal(room.Id, membership!.Id);
            Assert.False(membership.IsOwner);
            var identity = await guest.GetFromJsonAsync<SessionResponse>("/api/me");
            Assert.Equal("guest", identity!.Kind);
            Assert.Equal("Guest One", identity.DisplayName);
            Assert.Contains((await guest.GetFromJsonAsync<RoomResponse[]>("/api/rooms"))!, r => r.Id == room.Id);

            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.RoomInvitations.Where(i => i.Id == invitation.Id).Select(i => i.UseCount).SingleAsync());
            Assert.Equal(identity.GuestSessionId,
                await db.RoomMembers.Where(m => m.Id == membership.MemberId).Select(m => m.GuestSessionId).SingleAsync());

            await ApiFixture.CsrfAsync(secondGuest);
            await AccountTests.AssertProblem(
                await secondGuest.PostAsJsonAsync(invitation.JoinPath + "/join",
                    new JoinInvitationRequest { DisplayName = "Guest Two" }),
                HttpStatusCode.Gone, "invitation_unavailable");
            Assert.Equal(1, await db.RoomInvitations.Where(i => i.Id == invitation.Id).Select(i => i.UseCount).SingleAsync());
        }
    }

    [Fact]
    public async Task RegisteredUserJoinsRoomOnceAndRevokedInvitationStopsFurtherJoins()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        var (participant, participantUser, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        using (participant)
        {
            var room = await CreateRoomAsync(owner);
            var invitation = await CreateInvitationAsync(owner, room.Id, maxUses: 2);
            var first = await participant.PostAsJsonAsync(invitation.JoinPath + "/join", new JoinInvitationRequest());
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var second = await participant.PostAsJsonAsync(invitation.JoinPath + "/join", new JoinInvitationRequest());
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal((await first.Content.ReadFromJsonAsync<RoomResponse>())!.MemberId,
                (await second.Content.ReadFromJsonAsync<RoomResponse>())!.MemberId);
            Assert.Equal(HttpStatusCode.OK, (await participant.GetAsync($"/api/rooms/{room.Id}")).StatusCode);

            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.RoomInvitations.Where(i => i.Id == invitation.Id).Select(i => i.UseCount).SingleAsync());
            Assert.Equal(1, await db.RoomMembers.CountAsync(m => m.RoomId == room.Id && m.UserId == participantUser.UserId));

            Assert.Equal(HttpStatusCode.NoContent,
                (await owner.DeleteAsync($"/api/rooms/{room.Id}/invitations/{invitation.Id}")).StatusCode);
            await AccountTests.AssertProblem(await owner.GetAsync(invitation.JoinPath),
                HttpStatusCode.Gone, "invitation_unavailable");
        }
    }

    [Fact]
    public async Task CreatingNewInvitationRevokesPreviousAndReturnsWebLink()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        {
            var room = await CreateRoomAsync(owner);
            var initial = room.InitialInvitation!;
            var first = await CreateInvitationAsync(owner, room.Id);
            Assert.StartsWith("https://client.example/join/", first.JoinUrl);
            Assert.Equal("https://client.example/join/" + TokenOf(first), first.JoinUrl);
            await AccountTests.AssertProblem(await owner.GetAsync(initial.JoinPath),
                HttpStatusCode.Gone, "invitation_unavailable");

            var second = await CreateInvitationAsync(owner, room.Id, maxUses: 1);
            await AccountTests.AssertProblem(await owner.GetAsync(first.JoinPath),
                HttpStatusCode.Gone, "invitation_unavailable");
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(second.JoinPath)).StatusCode);

            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.RoomInvitations.CountAsync(i => i.RoomId == room.Id && i.RevokedAtUtc == null));
            Assert.Equal(0, await db.RoomInvitations.Where(i => i.Id == second.Id).Select(i => i.UseCount).SingleAsync());
            Assert.DoesNotContain(TokenOf(second),
                await db.RoomInvitations.Where(i => i.Id == second.Id).Select(i => i.TokenHash).SingleAsync());
        }
    }

    [Fact]
    public async Task ConcurrentGuestsCannotBothUseSingleUseInvitation()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        using (var firstGuest = fixture.Client())
        using (var secondGuest = fixture.Client())
        {
            var room = await CreateRoomAsync(owner);
            var invitation = await CreateInvitationAsync(owner, room.Id, maxUses: 1);
            await ApiFixture.CsrfAsync(firstGuest);
            await ApiFixture.CsrfAsync(secondGuest);
            var responses = await Task.WhenAll(
                firstGuest.PostAsJsonAsync("/api/invitations/join",
                    new JoinByTokenRequest { Token = TokenOf(invitation), DisplayName = "First Guest" }),
                secondGuest.PostAsJsonAsync("/api/invitations/join",
                    new JoinByTokenRequest { Token = TokenOf(invitation), DisplayName = "Second Guest" }));
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            await AccountTests.AssertProblem(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.OK),
                HttpStatusCode.Gone, "invitation_unavailable");

            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.RoomInvitations.Where(i => i.Id == invitation.Id).Select(i => i.UseCount).SingleAsync());
            Assert.Equal(1, await db.RoomMembers.CountAsync(m => m.RoomId == room.Id && m.GuestSessionId != null));
        }
    }

    [Fact]
    public async Task GuestsCannotCreateRoomsAndAnonymousJoinNeedsNameAndCsrf()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        var (existingGuest, _, _) = await fixture.GuestAsync();
        using (owner)
        using (existingGuest)
        using (var guest = fixture.Client())
        {
            var room = await CreateRoomAsync(owner);
            var invitation = await CreateInvitationAsync(owner, room.Id);
            await AccountTests.AssertProblem(
                await guest.PostAsJsonAsync(invitation.JoinPath + "/join", new JoinInvitationRequest { DisplayName = "Guest" }),
                HttpStatusCode.BadRequest, "invalid_csrf");
            await ApiFixture.CsrfAsync(guest);
            await AccountTests.AssertProblem(
                await guest.PostAsJsonAsync(invitation.JoinPath + "/join", new JoinInvitationRequest()),
                HttpStatusCode.BadRequest, "validation_failed");
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await guest.PostAsJsonAsync("/api/rooms", new CreateRoomRequest { Name = "Wrong" })).StatusCode);
            await ApiFixture.CsrfAsync(existingGuest);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await existingGuest.PostAsJsonAsync("/api/rooms", new CreateRoomRequest { Name = "Wrong" })).StatusCode);
        }
    }

    private static async Task<RoomResponse> CreateRoomAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest
        {
            Name = "Project Calendar", TimeZoneId = "Europe/Warsaw"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RoomResponse>())!;
    }

    private static async Task<RoomInvitationResponse> CreateInvitationAsync(HttpClient client, int roomId, int? maxUses = null)
    {
        var response = await client.PostAsJsonAsync($"/api/rooms/{roomId}/invitations",
            new CreateRoomInvitationRequest { MaxUses = maxUses });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RoomInvitationResponse>())!;
    }

    private static string TokenOf(RoomInvitationResponse invitation) =>
        invitation.JoinPath[(invitation.JoinPath.LastIndexOf('/') + 1)..];
}
