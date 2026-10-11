using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class RoomCoverService(AppDbContext db, RoomAccessService access, IRoomCoverStorage storage)
{
    private const long MaximumBytes = 10 * 1024 * 1024;

    public async Task<(Stream Stream, string ContentType)> OpenAsync(int roomId, CancellationToken cancellationToken)
    {
        var (room, _) = await access.RequireMemberAsync(roomId, cancellationToken);
        access.RequireReadable(room);
        if (room.CoverStorageKey is null) throw new ApiException(404, "cover_not_found", "Cover not found.");
        try { return (storage.OpenRead(room.CoverStorageKey), room.CoverContentType!); }
        catch (FileNotFoundException) { throw new ApiException(404, "cover_not_found", "Cover not found."); }
    }

    public async Task SaveAsync(int roomId, IFormFile file, CancellationToken cancellationToken)
    {
        var room = await access.RequireOwnerAsync(roomId, cancellationToken);
        access.RequireActive(room);
        if (file.Length is <= 0 or > MaximumBytes)
            throw ApiException.Validation(new Dictionary<string, string[]> { ["file"] = ["Cover must be 1–10 MB."] });
        await using var source = file.OpenReadStream();
        var header = new byte[12];
        if (await source.ReadAtLeastAsync(header, header.Length, false, cancellationToken) < 12)
            throw ApiException.Validation(new Dictionary<string, string[]> { ["file"] = ["Unsupported image format."] });
        var contentType = DetectType(header);
        if (contentType is null)
            throw ApiException.Validation(new Dictionary<string, string[]> { ["file"] = ["Use JPEG, PNG or WebP."] });
        await using var upload = file.OpenReadStream();
        var key = await storage.SaveAsync(upload, cancellationToken);
        var old = room.CoverStorageKey;
        try
        {
            room.CoverStorageKey = key;
            room.CoverContentType = contentType;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await storage.DeleteAsync(key);
            throw;
        }
        if (old is not null) await storage.DeleteAsync(old);
    }

    public async Task DeleteAsync(int roomId, CancellationToken cancellationToken)
    {
        var room = await access.RequireOwnerAsync(roomId, cancellationToken);
        access.RequireActive(room);
        var old = room.CoverStorageKey;
        if (old is null) return;
        room.CoverStorageKey = null;
        room.CoverContentType = null;
        await db.SaveChangesAsync(cancellationToken);
        await storage.DeleteAsync(old);
    }

    private static string? DetectType(ReadOnlySpan<byte> bytes)
    {
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
        if (bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }
}
