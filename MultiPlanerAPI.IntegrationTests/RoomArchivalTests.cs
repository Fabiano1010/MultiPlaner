using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Modules.Rooms;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class RoomArchivalTests(ApiFixture fixture)
{
    [Fact]
    public async Task AutomaticArchiveIsReadableBeforeWorkerRunsAndWorkerIsIdempotent()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        {
            var room = await CreateAsync(owner, true);
            var expiry = DateTimeOffset.UtcNow.AddMinutes(-1);
            await ExpireAsync(room.Id, expiry);

            var response = await owner.GetAsync($"/api/rooms/{room.Id}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var archived = (await response.Content.ReadFromJsonAsync<RoomResponse>())!;
            Assert.Equal(expiry, archived.ArchivedAtUtc);
            Assert.True(archived.ArchiveOnExpiry);
            Assert.DoesNotContain((await owner.GetFromJsonAsync<RoomResponse[]>("/api/rooms"))!, r => r.Id == room.Id);
            Assert.Contains((await owner.GetFromJsonAsync<RoomResponse[]>("/api/rooms?status=archived"))!, r => r.Id == room.Id);
            Assert.Contains((await owner.GetFromJsonAsync<RoomResponse[]>("/api/rooms?status=all"))!, r => r.Id == room.Id);
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/rooms/{room.Id}/members")).StatusCode);
            await AccountTests.AssertProblem(await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}",
                new UpdateRoomRequest { Name = "Must not change" }), HttpStatusCode.Conflict, "room_inactive");
            await AccountTests.AssertProblem(await owner.GetAsync(room.InitialInvitation!.JoinPath),
                HttpStatusCode.Gone, "invitation_unavailable");

            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null(await db.Rooms.Where(r => r.Id == room.Id).Select(r => r.ArchivedAtUtc).SingleAsync());
            var service = scope.ServiceProvider.GetRequiredService<RoomArchivalService>();
            await service.ArchiveDueAsync(default);
            var first = await db.Rooms.AsNoTracking().SingleAsync(r => r.Id == room.Id);
            Assert.Equal(expiry, first.ArchivedAtUtc);
            await service.ArchiveDueAsync(default);
            var second = await db.Rooms.AsNoTracking().SingleAsync(r => r.Id == room.Id);
            Assert.Equal(first.RowVersion, second.RowVersion);
        }
    }

    [Fact]
    public async Task ExpiredRoomWithoutOptInCannotBeRescuedByManualArchiving()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        {
            var room = await CreateAsync(owner, false);
            await ExpireAsync(room.Id, DateTimeOffset.UtcNow.AddMinutes(-1));
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<RoomArchivalService>().ArchiveDueAsync(default);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null(await db.Rooms.Where(r => r.Id == room.Id).Select(r => r.ArchivedAtUtc).SingleAsync());
            await AccountTests.AssertProblem(await owner.GetAsync($"/api/rooms/{room.Id}"),
                HttpStatusCode.Gone, "room_expired");
            await AccountTests.AssertProblem(await owner.PostAsync($"/api/rooms/{room.Id}/archive", null),
                HttpStatusCode.Conflict, "room_inactive");
            Assert.DoesNotContain((await owner.GetFromJsonAsync<RoomResponse[]>("/api/rooms?status=all"))!, r => r.Id == room.Id);
        }
    }

    [Fact]
    public async Task AutomaticArchiveFollowsEditedExpiryAndPreservesEarlyManualArchive()
    {
        var (owner, _, _) = await fixture.RegisterAndLoginAsync();
        using (owner)
        {
            var room = await CreateAsync(owner, true);
            var laterExpiry = DateTimeOffset.UtcNow.AddMonths(6);
            var response = await owner.PatchAsJsonAsync($"/api/rooms/{room.Id}",
                new UpdateRoomRequest { ExpiresAtUtc = laterExpiry });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var service = new RoomArchivalService(db, new FixedClock(laterExpiry.AddMonths(-1)));
            await service.ArchiveDueAsync(default);
            Assert.Null(await db.Rooms.Where(r => r.Id == room.Id).Select(r => r.ArchivedAtUtc).SingleAsync());

            Assert.Equal(HttpStatusCode.NoContent,
                (await owner.PostAsync($"/api/rooms/{room.Id}/archive", null)).StatusCode);
            var manualDate = await db.Rooms.Where(r => r.Id == room.Id).Select(r => r.ArchivedAtUtc).SingleAsync();
            Assert.NotNull(manualDate);
            await new RoomArchivalService(db, new FixedClock(laterExpiry)).ArchiveDueAsync(default);
            Assert.Equal(manualDate, await db.Rooms.Where(r => r.Id == room.Id).Select(r => r.ArchivedAtUtc).SingleAsync());
        }
    }

    private static async Task<RoomResponse> CreateAsync(HttpClient client, bool automaticArchive)
    {
        var response = await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest
        {
            Name = "Archival Test", ArchiveOnExpiry = automaticArchive
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RoomResponse>())!;
    }

    private async Task ExpireAsync(int roomId, DateTimeOffset expiry)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Rooms.Where(r => r.Id == roomId).ExecuteUpdateAsync(update => update
            .SetProperty(r => r.CreatedAtUtc, expiry.AddMonths(-3))
            .SetProperty(r => r.ExpiresAtUtc, expiry));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
