namespace MultiPlanerAPI.Modules.Rooms;

public sealed class RoomCoverService(
    RoomAccessService access,
    RoomCoverStorage storage,
    RoomMutationService mutations)
{
    private const long MaximumBytes = 10 * 1024 * 1024;

    public async Task<(Stream Stream, string ContentType)> OpenAsync(int roomId, CancellationToken ct)
    {
        var (room, _) = await access.RequireMemberAsync(roomId, ct);
        access.RequireReadable(room);
        return storage.OpenOrThrow(room.CoverStorageKey, room.CoverContentType,
            "cover_not_found", "Cover not found.");
    }

    public async Task SaveAsync(int roomId, IFormFile file, CancellationToken ct)
    {
        var room = await access.RequireManagerAsync(roomId, ct);
        access.RequireActive(room);
        await storage.SaveValidatedImageAsync(
            file, MaximumBytes, "Cover must be 1–10 MB.", "Unsupported image format.",
            (key, contentType, token) => ReplaceAsync(roomId, key, contentType, token), ct);
    }

    public async Task DeleteAsync(int roomId, CancellationToken ct)
    {
        storage.Delete(await ReplaceAsync(roomId, null, null, ct));
    }

    private Task<string?> ReplaceAsync(int roomId, string? key, string? contentType, CancellationToken ct)
    {
        return mutations.ExecuteAsManagerAsync(roomId, async (context, room, _) =>
        {
            var oldKey = room.CoverStorageKey;
            room.CoverStorageKey = key;
            room.CoverContentType = contentType;
            await context.SaveChangesAsync(ct);
            return oldKey;
        }, ct);
    }
}