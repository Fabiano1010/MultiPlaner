using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;

namespace MultiPlanerAPI.Modules.Users;

public sealed class AvatarService(
    AppDbContext db,
    DbContextOptions<AppDbContext> options,
    CurrentActor actor,
    TimeProvider clock,
    AvatarStorage storage)
{
    public const long MaxBytes = 5 * 1024 * 1024;

    public async Task SaveAsync(IFormFile file, CancellationToken ct)
    {
        actor.RequireUserId();
        if (file.Length is <= 0 or > MaxBytes)
        {
            throw Invalid("Avatar must be between 1 byte and 5 MB.");
        }

        await using var source = file.OpenReadStream();
        var header = new byte[12];
        if (await source.ReadAtLeastAsync(header, 12, false, ct) < 12)
        {
            throw Invalid("Use JPEG, PNG or WebP.");
        }

        var type = DetectType(header) ?? throw Invalid("Use JPEG, PNG or WebP.");
        await using var upload = file.OpenReadStream();
        var key = await storage.SaveAsync(upload, ct);
        string? old;
        try
        {
            old = await ReplaceAsync(key, type, ct);
        }
        catch
        {
            storage.Delete(key);
            throw;
        }

        storage.Delete(old);
    }

    public async Task DeleteAsync(CancellationToken ct)
    {
        storage.Delete(await ReplaceAsync(null, null, ct));
    }

    private Task<string?> ReplaceAsync(string? key, string? type, CancellationToken ct)
    {
        return db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var context = new AppDbContext(options, clock);
            await using var tx = await context.Database.BeginTransactionAsync(ct);
            var id = actor.RequireUserId();
            var user = await context.Users
                .FromSqlInterpolated($"SELECT * FROM [dbo].[AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {id}")
                .SingleAsync(ct);
            var old = user.AvatarStorageKey;
            user.AvatarStorageKey = key;
            user.AvatarContentType = type;
            user.AvatarUrl = null;
            user.ConcurrencyStamp = Guid.NewGuid().ToString();
            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return old;
        });
    }

    public async Task<(Stream Stream, string Type)> OpenAsync(int userId, CancellationToken ct)
    {
        if (actor.UserId != userId)
        {
            var viewerRooms = db.RoomMembers.Where(m => m.LeftAtUtc == null &&
                                                        ((actor.UserId != null && m.UserId == actor.UserId) ||
                                                         (actor.GuestSessionId != null &&
                                                          m.GuestSessionId == actor.GuestSessionId)))
                .Select(m => m.RoomId);
            var now = clock.GetUtcNow();
            if (!await (from m in db.RoomMembers
                    join r in db.Rooms on m.RoomId equals r.Id
                    where m.UserId == userId && m.LeftAtUtc == null && viewerRooms.Contains(m.RoomId) &&
                          (r.ExpiresAtUtc > now || r.ArchivedAtUtc != null || r.ArchiveOnExpiry)
                    select m.Id).AnyAsync(ct))
            {
                throw new ApiException(404, "avatar_not_found", "Avatar not found.");
            }
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user?.AvatarStorageKey == null)
        {
            throw new ApiException(404, "avatar_not_found", "Avatar not found.");
        }

        try
        {
            return (storage.Open(user.AvatarStorageKey), user.AvatarContentType!);
        }
        catch (FileNotFoundException)
        {
            throw new ApiException(404, "avatar_not_found", "Avatar not found.");
        }
    }

    private static ApiException Invalid(string message)
    {
        return ApiException.Validation(new Dictionary<string, string[]> { ["file"] = [message] });
    }

    internal static string? DetectType(ReadOnlySpan<byte> h)
    {
        if (h.Length < 12)
        {
            return null;
        }

        if (h[0] == 255 && h[1] == 216 && h[2] == 255)
        {
            return "image/jpeg";
        }

        if (h[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            return "image/png";
        }

        if (h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}