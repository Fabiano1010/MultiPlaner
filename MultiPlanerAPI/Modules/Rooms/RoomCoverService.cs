using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
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

    public async Task<(Stream Stream, string ContentType)> OpenAsync(int roomId, CancellationToken cancellationToken)
    {
        var (room, _) = await access.RequireMemberAsync(roomId, cancellationToken);
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

    public async Task SaveAsync(int roomId, IFormFile file, CancellationToken cancellationToken)
    {
        var room = await access.RequireManagerAsync(roomId, cancellationToken);
        access.RequireActive(room);
        if (file.Length is <= 0 or > MaximumBytes)
        {
            throw ApiException.Validation(new Dictionary<string, string[]> { ["file"] = ["Cover must be 1–10 MB."] });
        }

        await using var source = file.OpenReadStream();
        var header = new byte[12];
        if (await source.ReadAtLeastAsync(header, header.Length, false, cancellationToken) < 12)
        {
            throw ApiException.Validation(new Dictionary<string, string[]>
                { ["file"] = ["Unsupported image format."] });
        }

        var contentType = DetectType(header);
        if (contentType is null)
        {
            throw ApiException.Validation(new Dictionary<string, string[]> { ["file"] = ["Use JPEG, PNG or WebP."] });
        }

        await using var upload = file.OpenReadStream();
        var key = await storage.SaveAsync(upload, cancellationToken);
        string? old;
        try
        {
            old = await ReplaceAsync(roomId, key, contentType, cancellationToken);
        }
        catch
        {
            await storage.DeleteAsync(key);
            throw;
        }

        if (old is not null)
        {
            await storage.DeleteAsync(old);
        }
    }

    public async Task DeleteAsync(int roomId, CancellationToken cancellationToken)
    {
        var old = await ReplaceAsync(roomId, null, null, cancellationToken);
        if (old is not null)
        {
            await storage.DeleteAsync(old);
        }
    }

    private Task<string?> ReplaceAsync(int roomId, string? key, string? contentType, CancellationToken ct)
    {
        return mutations.ExecuteAsync(roomId, async (context, room) =>
        {
            var roomAccess = new RoomAccessService(context, actor, clock);
            await roomAccess.RequireManagerAsync(roomId, ct);
            roomAccess.RequireActive(room);
            var old = room.CoverStorageKey;
            room.CoverStorageKey = key;
            room.CoverContentType = contentType;
            await context.SaveChangesAsync(ct);
            return old;
        }, ct);
    }

    private static string? DetectType(ReadOnlySpan<byte> bytes)
    {
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
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