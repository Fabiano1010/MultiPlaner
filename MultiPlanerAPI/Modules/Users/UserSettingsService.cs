using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerSharedModels.Contracts.Users;
namespace MultiPlanerAPI.Modules.Users;
public sealed class UserSettingsService(AppDbContext db, DbContextOptions<AppDbContext> options, CurrentActor actor, TimeProvider clock)
{
    public async Task<ThemeResponse> GetAsync(CancellationToken ct) {
        var id = actor.RequireUserId();
        var theme = await db.UserThemes.AsNoTracking().SingleOrDefaultAsync(t => t.UserId == id, ct);
        return new(theme?.Palette ?? new(), theme?.RowVersion ?? []);
    }
    public Task<ThemeResponse> SaveAsync(ThemePalette palette, byte[] version, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () => {
            await using var context = new AppDbContext(options, clock);
            await using var tx = await context.Database.BeginTransactionAsync(ct);
            var id = actor.RequireUserId();
            await context.Users.FromSqlInterpolated($"SELECT * FROM [dbo].[AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}").SingleAsync(ct);
            var theme = await context.UserThemes.SingleOrDefaultAsync(t => t.UserId == id, ct);
            if (!(theme?.RowVersion ?? []).AsSpan().SequenceEqual(version))
                throw new ApiException(409, "concurrency_conflict", "Theme changed. Reload and retry.");
            if (theme == null) { theme = new UserTheme { UserId = id }; context.UserThemes.Add(theme); }
            theme.Palette = palette with {};
            context.Entry(theme).Property(t => t.UpdatedAtUtc).IsModified = theme.RowVersion.Length > 0;
            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new ThemeResponse(theme.Palette, theme.RowVersion);
        });
}
