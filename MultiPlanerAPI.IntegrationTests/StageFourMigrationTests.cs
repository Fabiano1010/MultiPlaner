using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MultiPlanerAPI.Data;
namespace MultiPlanerAPI.IntegrationTests;
[Collection(ApiCollection.Name)]
public sealed class StageFourMigrationTests(ApiFixture fixture)
{
    [Fact] public async Task FreshDatabaseContainsSettingsAndTypesAndMatchesSnapshot() {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(db.Database.HasPendingModelChanges());
        await db.RoomEventTypes.Take(1).ToListAsync();
        await db.UserThemes.Take(1).ToListAsync();
        await DatabaseInitializer.MigrateAsync(db);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
