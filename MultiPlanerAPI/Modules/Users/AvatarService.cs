using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Modules.Rooms;

namespace MultiPlanerAPI.Modules.Users;

public sealed class AvatarService(
    AppDbContext db,
    RoomMutationService mutations,
    CurrentActor actor,
    TimeProvider clock,
    AvatarStorage storage)
{
    public const long MaxBytes = 5 * 1024 * 1024;

    public Task SaveAsync(IFormFile file, CancellationToken ct)
    {
        actor.RequireUserId();
        return storage.SaveValidatedImageAsync(
            file, MaxBytes, "Avatar must be between 1 byte and 5 MB.", "Use JPEG, PNG or WebP.", ReplaceAsync, ct);
    }

    public async Task DeleteAsync(CancellationToken ct)
    {
        storage.Delete(await ReplaceAsync(null, null, ct));
    }

    public async Task<(Stream Stream, string Type)> OpenAsync(int userId, CancellationToken ct)
    {
        if (actor.UserId != userId && !await SharesReadableRoomAsync(userId, ct))
        {
            throw new ApiException(404, "avatar_not_found", "Avatar not found.");
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct);
        return storage.OpenOrThrow(user?.AvatarStorageKey, user?.AvatarContentType,
            "avatar_not_found", "Avatar not found.");
    }

    private Task<bool> SharesReadableRoomAsync(int targetUserId, CancellationToken ct)
    {
        var viewerRooms = db.RoomMembers
            .Where(m => m.LeftAtUtc == null &&
                        ((actor.UserId != null && m.UserId == actor.UserId) ||
                         (actor.GuestSessionId != null && m.GuestSessionId == actor.GuestSessionId)))
            .Select(m => m.RoomId);
        var now = clock.GetUtcNow();
        return (from m in db.RoomMembers
            join r in db.Rooms on m.RoomId equals r.Id
            where m.UserId == targetUserId && m.LeftAtUtc == null && viewerRooms.Contains(m.RoomId) &&
                  (r.ExpiresAtUtc > now || r.ArchivedAtUtc != null || r.ArchiveOnExpiry)
            select m.Id).AnyAsync(ct);
    }

    private Task<string?> ReplaceAsync(string? key, string? type, CancellationToken ct)
    {
        return mutations.ExecuteForUserAsync(actor.RequireUserId(), async (context, user) =>
        {
            var old = user.AvatarStorageKey;
            user.AvatarStorageKey = key;
            user.AvatarContentType = type;
            user.AvatarUrl = null;
            user.ConcurrencyStamp = Guid.NewGuid().ToString();
            await context.SaveChangesAsync(ct);
            return old;
        }, ct);
    }

    internal static string? BuildUrl(int? userId, string? storageKey, string? fallbackUrl) =>
        userId is null ? null
        : storageKey is not null ? $"/api/users/{userId}/avatar?v={storageKey}"
        : fallbackUrl;

    internal static string? DetectType(ReadOnlySpan<byte> h) => LocalFileStorage.DetectImageType(h);
}