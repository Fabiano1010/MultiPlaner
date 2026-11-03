using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MultiPlanerAPI.Data;

namespace MultiPlanerAPI.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class StageFourMigrationTests(ApiFixture fixture)
{
    [Fact]
    public async Task FreshDatabaseContainsSettingsAndTypesAndMatchesSnapshot()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(db.Database.HasPendingModelChanges());
        await db.RoomEventTypes.Take(1).ToListAsync();
        await db.UserThemes.Take(1).ToListAsync();
        await DatabaseInitializer.MigrateAsync(db);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task ExistingPaletteIsConvertedWithoutResettingDatabase()
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var connection = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(source.Database.GetConnectionString())
            { InitialCatalog = "MultiPlanerTests_Themes_" + Guid.NewGuid().ToString("N") };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection.ConnectionString).Options;
        await using var db = new AppDbContext(options, TimeProvider.System);
        try
        {
            var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
                .GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
            var before = db.Database.GetMigrations().First();
            await migrator.MigrateAsync(before);
            var user = new Models.ApplicationUser
            {
                Email = "migration@example.test", NormalizedEmail = "MIGRATION@EXAMPLE.TEST", UserName = "migration",
                DisplayName = "Existing user"
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var now = DateTimeOffset.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                                                           INSERT INTO [dbo].[UserThemes] ([UserId], [Palette_Primary], [Palette_PanelBackground], [Palette_ControlBackground], [Palette_Secondary], [Palette_Background], [Palette_RoomSelection], [Palette_DaySelection], [Palette_DaySelectionBackground], [CreatedAtUtc], [UpdatedAtUtc])
                                                           VALUES ({user.Id}, '#112233', '#CCCCCC', '#445566', '#000000', '#778899', '#43D5FF', '#AABBCC', '#EDE7F6', {now}, {now});
                                                           """);
            await migrator.MigrateAsync();
            var saved = await db.SavedThemes.SingleAsync();
            var active = await db.UserThemes.SingleAsync();
            Assert.Equal("Mój schemat", saved.Name);
            Assert.Equal(saved.Id, active.ActiveThemeId);
            Assert.Equal("#778899", saved.Palette.Background);
            Assert.Equal("#445566", saved.Palette.Surface);
            Assert.Equal("#AABBCC", saved.Palette.Accent);
            Assert.Equal("#112233", saved.Palette.Border);
            Assert.Equal(saved.Palette, active.Palette);
            Assert.Equal("Existing user", (await db.Users.SingleAsync()).DisplayName);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}