using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerSharedModels.Contracts.Users;

namespace MultiPlanerAPI.Modules.Users;

public sealed class UserSettingsService(
    AppDbContext db,
    DbContextOptions<AppDbContext> options,
    CurrentActor actor,
    TimeProvider clock)
{
    private static SavedThemeResponse BuiltIn(int id)
    {
        return id switch
        {
            -1 => new SavedThemeResponse(-1, "Default", new ThemePalette(), [], true),
            -2 => new SavedThemeResponse(-2, "Dark",
                new ThemePalette
                    { Background = "#161616", Surface = "#303030", Accent = "#A78BFA", Border = "#666666" }, [],
                true),
            _ => throw new ApiException(404, "theme_not_found", "Theme not found.")
        };
    }

    public async Task<ThemeResponse> GetAsync(CancellationToken ct)
    {
        var id = actor.RequireUserId();
        var value = await db.UserThemes.AsNoTracking().SingleOrDefaultAsync(t => t.UserId == id, ct);
        return new ThemeResponse(value?.Palette ?? new ThemePalette(), value?.RowVersion ?? [],
            value?.ActiveThemeId ?? -1);
    }

    public async Task<IReadOnlyList<SavedThemeResponse>> ListAsync(CancellationToken ct)
    {
        var id = actor.RequireUserId();
        var rows = await db.SavedThemes.AsNoTracking().Where(t => t.UserId == id).OrderBy(t => t.Id).ToListAsync(ct);
        return new[] { BuiltIn(-1), BuiltIn(-2) }.Concat(rows.Select(Map)).ToArray();
    }

    private Task<T> Mutate<T>(Func<AppDbContext, int, Task<T>> operation, CancellationToken ct)
    {
        return db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var context = new AppDbContext(options, clock);
            await using var tx = await context.Database.BeginTransactionAsync(ct);
            var id = actor.RequireUserId();
            await context.Users
                .FromSqlInterpolated($"SELECT * FROM [dbo].[AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
                .SingleAsync(ct);
            var result = await operation(context, id);
            await tx.CommitAsync(ct);
            return result;
        });
    }

    public Task<SavedThemeResponse> SaveAsync(int? themeId, SaveThemeRequest request, CancellationToken ct)
    {
        return Mutate(async (context, userId) =>
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
            {
                throw ApiException.Validation(new Dictionary<string, string[]>
                    { ["name"] = ["Name cannot be blank."] });
            }

            SavedTheme theme;
            if (themeId == null)
            {
                theme = new SavedTheme { UserId = userId };
                context.SavedThemes.Add(theme);
            }
            else
            {
                theme = await Find(context, userId, themeId.Value, ct);
                Check(theme.RowVersion, request.RowVersion);
            }

            theme.Name = name;
            theme.Palette = request.Palette with { };
            if (themeId != null)
            {
                context.Entry(theme).Property(t => t.UpdatedAtUtc).IsModified = true;
            }

            await context.SaveChangesAsync(ct);
            return Map(theme);
        }, ct);
    }

    public Task<ThemeResponse> ApplyAsync(ApplyThemeRequest request, CancellationToken ct)
    {
        return Mutate(async (context, userId) =>
        {
            SavedThemeResponse chosen;
            if (request.ThemeId < 0)
            {
                chosen = BuiltIn(request.ThemeId);
            }
            else
            {
                var row = await Find(context, userId, request.ThemeId, ct);
                Check(row.RowVersion, request.RowVersion);
                chosen = Map(row);
            }

            var active = await context.UserThemes.SingleOrDefaultAsync(t => t.UserId == userId, ct);
            Check(active?.RowVersion ?? [], request.ActiveRowVersion);
            if (active == null)
            {
                active = new UserTheme { UserId = userId };
                context.UserThemes.Add(active);
            }

            active.ActiveThemeId = chosen.Id;
            active.Palette = chosen.Palette with { };
            if (active.RowVersion.Length > 0)
            {
                context.Entry(active).Property(t => t.UpdatedAtUtc).IsModified = true;
            }

            await context.SaveChangesAsync(ct);
            return new ThemeResponse(active.Palette, active.RowVersion, active.ActiveThemeId);
        }, ct);
    }

    public Task DeleteAsync(int themeId, byte[] version, CancellationToken ct)
    {
        return Mutate(async (context, userId) =>
        {
            var row = await Find(context, userId, themeId, ct);
            Check(row.RowVersion, version);
            var active = await context.UserThemes.SingleOrDefaultAsync(t => t.UserId == userId, ct);
            if (active?.ActiveThemeId == themeId)
            {
                active.ActiveThemeId = -1;
                active.Palette = new ThemePalette();
            }

            context.SavedThemes.Remove(row);
            await context.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    private static async Task<SavedTheme> Find(AppDbContext context, int userId, int id, CancellationToken ct)
    {
        return await context.SavedThemes.SingleOrDefaultAsync(t => t.Id == id && t.UserId == userId, ct)
               ?? throw new ApiException(404, "theme_not_found", "Theme not found or is protected.");
    }

    private static void Check(byte[] actual, byte[] expected)
    {
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new ApiException(409, "concurrency_conflict", "Theme changed. Reload and retry.");
        }
    }

    private static SavedThemeResponse Map(SavedTheme t)
    {
        return new SavedThemeResponse(t.Id, t.Name, t.Palette, t.RowVersion);
    }
}