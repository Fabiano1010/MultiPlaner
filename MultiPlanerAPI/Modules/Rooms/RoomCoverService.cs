using MultiPlanerAPI.Infrastructure;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class RoomCoverService(
    RoomAccessService access,
    RoomCoverStorage storage,
    RoomMutationService mutations,
    Users.CurrentActor actor,
    TimeProvider clock)
{
    private const long MaximumBytes = 10 * 1024 * 1024;

    public async Task<(Stream Stream, string ContentType)> OpenAsync(int roomId, CancellationToken ct)
    {
        var (room, _) = await access.RequireMemberAsync(roomId, ct);
        access.RequireReadable(room);
        if (room.CoverStorageKey is null)
        {
            throw new ApiException(404, "cover_not_found", "Cover not found.");
        }

        try
        {
            return (storage.Open(room.CoverStorageKey), room.CoverContentType!);
        }
        catch (FileNotFoundException)
        {
            throw new ApiException(404, "cover_not_found", "Cover not found.");
        }
    }

    public async Task SaveAsync(int roomId, IFormFile file, CancellationToken ct)
    {
        var room = await access.RequireManagerAsync(roomId, ct);
        access.RequireActive(room);
        if (file.Length is <= 0 or > MaximumBytes)
        {
            throw Invalid("Cover must be 1–10 MB.");
        }

        await using var source = file.OpenReadStream();
        var header = new byte[12];
        if (await source.ReadAtLeastAsync(header, header.Length, false, ct) < 12)
        {
            throw Invalid("Unsupported image format.");
        }

        var contentType = Users.AvatarService.DetectType(header) ?? throw Invalid("Use JPEG, PNG or WebP.");

        await using var upload = file.OpenReadStream();
        var key = await storage.SaveAsync(upload, ct);
        string? oldKey;
        try
        {
            oldKey = await ReplaceAsync(roomId, key, contentType, ct);
        }
        catch
        {
            storage.Delete(key);
            throw;
        }

        storage.Delete(oldKey);
    }

    public async Task DeleteAsync(int roomId, CancellationToken ct)
    {
        storage.Delete(await ReplaceAsync(roomId, null, null, ct));
    }

    private Task<string?> ReplaceAsync(int roomId, string? key, string? contentType, CancellationToken ct)
    {
        return mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var roomAccess = new RoomAccessService(context, actor, clock);
            await roomAccess.RequireManagerAsync(room, ct);
            roomAccess.RequireActive(room);
            var oldKey = room.CoverStorageKey;
            room.CoverStorageKey = key;
            room.CoverContentType = contentType;
            await context.SaveChangesAsync(ct);
            return oldKey;
        }, ct);
    }

    private static ApiException Invalid(string message)
    {
        return ApiException.Validation(new Dictionary<string, string[]> { ["file"] = [message] });
    }
}