using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Models;

namespace MultiPlanerAPI.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class StageFourMigrationTests(ApiFixture fixture)
{
    [Fact]
    public async Task ExistingStageThreeDatabaseUpgradesWithoutLosingRoomOrTimedEvent()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = new SqlConnectionStringBuilder(source.Database.GetConnectionString())
            { InitialCatalog = "MultiPlanerTests_Upgrade_" + Guid.NewGuid().ToString("N") };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection.ConnectionString).Options;
        await using var db = new AppDbContext(options, TimeProvider.System);
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("20261011134010_InitialApiSchema");
            var user = new ApplicationUser
            {
                DisplayName = "Migration Owner", UserName = "migration-owner",
                Email = "migration@example.com", NormalizedEmail = "MIGRATION@EXAMPLE.COM"
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var room = new Room { OwnerUserId = user.Id, Name = "Existing room", ExpiresAtUtc = DateTimeOffset.UtcNow.AddMonths(1) };
            db.Rooms.Add(room);
            await db.SaveChangesAsync();
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [dbo].[RoomMembers]
                    ([RoomId], [UserId], [DisplayName], [Color], [IsFavourite], [NotificationsMuted], [CreatedAtUtc], [UpdatedAtUtc])
                VALUES ({room.Id}, {user.Id}, N'Migration Owner', 'AABBCC', 0, 0, {now}, {now});
                INSERT INTO [dbo].[CalendarEvents]
                    ([RoomId], [AuthorMemberId], [Title], [StartsAtUtc], [EndsAtUtc], [IsAllDay], [Category], [IsPinned], [CreatedAtUtc], [UpdatedAtUtc])
                SELECT {room.Id}, [Id], N'Existing event', {now}, {now.AddHours(1)}, 0, 'Meeting', 1, {now}, {now}
                FROM [dbo].[RoomMembers] WHERE [RoomId] = {room.Id};
                """);
            await DatabaseInitializer.MigrateAsync(db);
            Assert.False(db.Database.HasPendingModelChanges());
            var ev = await db.CalendarEvents.SingleAsync();
            Assert.Equal("Existing event", ev.Title);
            Assert.True(ev.IsPinned);
            Assert.False(ev.IsHighPriority);
            Assert.Null((await db.RoomMembers.SingleAsync()).LeftAtUtc);
            Assert.Equal("Existing room", (await db.Rooms.SingleAsync()).Name);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
