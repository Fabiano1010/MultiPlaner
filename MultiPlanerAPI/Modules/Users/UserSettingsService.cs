using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Rooms;
using MultiPlanerSharedModels.Contracts.Users;

namespace MultiPlanerAPI.Modules.Users;

public sealed class UserSettingsService(
    AppDbContext db,
    RoomMutationService mutations,
    CurrentActor actor)
{
    private static SavedThemeResponse BuiltIn(int id)
    {
        return id switch
        {
            -1 => new SavedThemeResponse(-1, "Default", new ThemePalette(), [], true),
            -2 => new SavedThemeResponse(-2, "Dark",
                new ThemePalette
                    { Background = "#161616", Surface = "#303030", Accent = "#A78BFA", Border = "#666666" },
                [], true),
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
        return [BuiltIn(-1), BuiltIn(-2), .. rows.Select(Map)];
    }

    private Task<T> Mutate<T>(Func<AppDbContext, int, Task<T>> operation, CancellationToken ct)
    {
        return mutations.ExecuteForUserAsync(actor.RequireUserId(), (context, user) => operation(context, user.Id), ct);
    }

    public Task<SavedThemeResponse> SaveAsync(int? themeId, SaveThemeRequest request, CancellationToken ct)
    {
        return Mutate(async (context, userId) =>
        {
            var name = request.Name.Trim();
            if (name.Length == 0)
            {
                throw ApiException.Validation("name", "Name cannot be blank.");
            }

            SavedTheme theme;
            if (themeId is { } id)
            {
                theme = await Find(context, userId, id, ct);
                ApiException.RequireVersion(theme.RowVersion, request.RowVersion, "Theme changed. Reload and retry.");
                context.Entry(theme).Property(t => t.UpdatedAtUtc).IsModified = true;
            }
            else
            {
                theme = new SavedTheme { UserId = userId };
                context.SavedThemes.Add(theme);
            }

            theme.Name = name;
            theme.Palette = request.Palette with { };
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
                ApiException.RequireVersion(row.RowVersion, request.RowVersion, "Theme changed. Reload and retry.");
                chosen = Map(row);
            }

            var active = await context.UserThemes.SingleOrDefaultAsync(t => t.UserId == userId, ct);
            ApiException.RequireVersion(active?.RowVersion ?? [], request.ActiveRowVersion,
                "Theme changed. Reload and retry.");
            if (active is null)
            {
                active = new UserTheme { UserId = userId };
                context.UserThemes.Add(active);
            }
            else
            {
                context.Entry(active).Property(t => t.UpdatedAtUtc).IsModified = true;
            }

            active.ActiveThemeId = chosen.Id;
            active.Palette = chosen.Palette with { };
            await context.SaveChangesAsync(ct);
            return new ThemeResponse(active.Palette, active.RowVersion, active.ActiveThemeId);
        }, ct);
    }

    public Task DeleteAsync(int themeId, byte[] version, CancellationToken ct)
    {
        return Mutate(async (context, userId) =>
        {
            var row = await Find(context, userId, themeId, ct);
            ApiException.RequireVersion(row.RowVersion, version, "Theme changed. Reload and retry.");
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

    private static SavedThemeResponse Map(SavedTheme t)
    {
        return new SavedThemeResponse(t.Id, t.Name, t.Palette, t.RowVersion);
    }
}