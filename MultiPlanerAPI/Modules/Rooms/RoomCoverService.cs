using MultiPlanerAPI.Infrastructure;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class RoomCoverService(
    RoomAccessService access,
    IRoomCoverStorage storage,
    RoomMutationService mutations,
    Users.CurrentActor actor,
    TimeProvider clock)
{
    private const long MaximumBytes = 10 * 1024 * 1024;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

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
            return (storage.OpenRead(room.CoverStorageKey), room.CoverContentType!);
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
            throw ApiException.Validation(new Dictionary<string, string[]> { ["file"] = ["Cover must be 1–10 MB."] });
        }

        await using var source = file.OpenReadStream();
        var header = new byte[12];
        if (await source.ReadAtLeastAsync(header, header.Length, false, ct) < 12)
        {
            throw ApiException.Validation(new Dictionary<string, string[]>
                { ["file"] = ["Unsupported image format."] });
        }

        var contentType = DetectType(header)
                          ?? throw ApiException.Validation(new Dictionary<string, string[]>
                              { ["file"] = ["Use JPEG, PNG or WebP."] });

        await using var upload = file.OpenReadStream();
        var key = await storage.SaveAsync(upload, ct);
        string? oldKey;
        try
        {
            oldKey = await ReplaceAsync(roomId, key, contentType, ct);
        }
        catch
        {
            await storage.DeleteAsync(key);
            throw;
        }

        if (oldKey is not null)
        {
            await storage.DeleteAsync(oldKey);
        }
    }

    public async Task DeleteAsync(int roomId, CancellationToken ct)
    {
        var oldKey = await ReplaceAsync(roomId, null, null, ct);
        if (oldKey is not null)
        {
            await storage.DeleteAsync(oldKey);
        }
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

    private static string? DetectType(ReadOnlySpan<byte> bytes)
    {
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes[..8].SequenceEqual(PngSignature))
        {
            return "image/png";
        }

        if (bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}