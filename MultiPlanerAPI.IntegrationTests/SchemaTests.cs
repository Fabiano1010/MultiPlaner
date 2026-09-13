using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Models;

namespace MultiPlanerAPI.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SchemaTests(ApiFixture fixture)
{
    [Fact]
    public async Task InitialMigrationIsAppliedAndMatchesTheModel()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.EndsWith("_InitialApiSchema", Assert.Single(applied));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        // A second migration pass must be harmless.
        await DatabaseInitializer.MigrateAsync(db);
    }

    [Fact]
    public async Task MemberMustHaveExactlyOneIdentity()
    {
        var data = await CreateRoomAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.RoomMembers.Add(new RoomMember { RoomId = data.RoomId, DisplayName = "No Identity" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var guest = new GuestSession { DisplayName = "Guest", ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(30) };
        db.GuestSessions.Add(guest);
        await db.SaveChangesAsync();
        db.RoomMembers.Add(new RoomMember
        {
            RoomId = data.RoomId, UserId = data.UserId, GuestSessionId = guest.Id, DisplayName = "Two Identities"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateMembershipIsRejected()
    {
        var data = await CreateRoomAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.RoomMembers.Add(new RoomMember { RoomId = data.RoomId, UserId = data.UserId, DisplayName = "Duplicate" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task EventCannotUseAnAuthorFromAnotherRoom()
    {
        var first = await CreateRoomAsync();
        var second = await CreateRoomAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CalendarEvents.Add(Event(second.RoomId, first.MemberId));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ParticipantCannotComeFromAnotherRoom()
    {
        var first = await CreateRoomAsync();
        var second = await CreateRoomAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = Event(first.RoomId, first.MemberId);
        db.CalendarEvents.Add(entry);
        await db.SaveChangesAsync();
        db.EventParticipants.Add(new EventParticipant { EventId = entry.Id, RoomId = first.RoomId, MemberId = second.MemberId });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task EventMustHavePositiveDuration()
    {
        var data = await CreateRoomAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = Event(data.RoomId, data.MemberId);
        entry.EndsAtUtc = entry.StartsAtUtc;
        db.CalendarEvents.Add(entry);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task RowVersionDetectsConcurrentEventUpdates()
    {
        var data = await CreateRoomAsync();
        await using var scope1 = fixture.Factory.Services.CreateAsyncScope();
        await using var scope2 = fixture.Factory.Services.CreateAsyncScope();
        var first = scope1.ServiceProvider.GetRequiredService<AppDbContext>();
        var second = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = Event(data.RoomId, data.MemberId);
        first.CalendarEvents.Add(entry);
        await first.SaveChangesAsync();
        var stale = await second.CalendarEvents.SingleAsync(e => e.Id == entry.Id);
        entry.Title = "First update";
        await first.SaveChangesAsync();
        stale.Title = "Second update";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task ChatStoresConcurrentMessagesWithoutOverwritingAndRejectsRetries()
    {
        var data = await CreateRoomAsync();
        var ids = await Task.WhenAll(Enumerable.Range(0, 8).Select(async index =>
        {
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = new ChatMessage
            {
                RoomId = data.RoomId, AuthorMemberId = data.MemberId, Text = $"Message {index}",
                ClientRequestId = Guid.NewGuid(), SentAtUtc = DateTimeOffset.UtcNow
            };
            db.ChatMessages.Add(message);
            await db.SaveChangesAsync();
            return message.ClientRequestId;
        }));
        await using var finalScope = fixture.Factory.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(8, await finalDb.ChatMessages.CountAsync(m => m.RoomId == data.RoomId));
        finalDb.ChatMessages.Add(new ChatMessage
        {
            RoomId = data.RoomId, AuthorMemberId = data.MemberId, Text = "Retried message",
            ClientRequestId = ids[0], SentAtUtc = DateTimeOffset.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => finalDb.SaveChangesAsync());
    }

    [Fact]
    public async Task RoomWithContentsCannotBeDeletedAccidentally()
    {
        var data = await CreateRoomAsync();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Rooms.Remove(await db.Rooms.SingleAsync(r => r.Id == data.RoomId));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private async Task<(int UserId, int RoomId, int MemberId)> CreateRoomAsync()
    {
        var (client, user, _) = await fixture.RegisterAndLoginAsync();
        client.Dispose();
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var room = new Room { OwnerUserId = user.UserId!.Value, Name = "Test Room", ExpiresAtUtc = DateTimeOffset.UtcNow.AddMonths(3) };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        var member = new RoomMember { RoomId = room.Id, UserId = user.UserId, DisplayName = "Owner" };
        db.RoomMembers.Add(member);
        await db.SaveChangesAsync();
        return (user.UserId.Value, room.Id, member.Id);
    }

    private static CalendarEvent Event(int roomId, int authorMemberId) => new()
    {
        RoomId = roomId, AuthorMemberId = authorMemberId, Title = "Meeting",
        StartsAtUtc = DateTimeOffset.UtcNow, EndsAtUtc = DateTimeOffset.UtcNow.AddHours(1)
    };
}
