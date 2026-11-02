using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Events;
namespace MultiPlanerAPI.Modules.Rooms;
public sealed class EventTypeService(AppDbContext db, RoomAccessService access, RoomMutationService mutations, CurrentActor actor, TimeProvider clock)
{
    public static void AddDefaults(AppDbContext db, int roomId) => db.RoomEventTypes.AddRange(
        new[] { "Meeting", "Training", "Social", "Business trip", "Leave", "Shift" }.Select(name =>
            new RoomEventType { RoomId = roomId, Name = name, NormalizedName = name.ToUpperInvariant() }));

    public async Task<IReadOnlyList<EventTypeResponse>> ListAsync(int roomId, CancellationToken ct)
    {
        var (room, _) = await access.RequireMemberAsync(roomId, ct);
        access.RequireReadable(room);
        return await db.RoomEventTypes.AsNoTracking().Where(t => t.RoomId == roomId).OrderBy(t => t.Id)
            .Select(t => new EventTypeResponse(t.Id, t.RoomId, t.Name, t.Color, t.IsArchived, t.RowVersion)).ToArrayAsync(ct);
    }
    public Task<EventTypeResponse> CreateAsync(int roomId, CreateEventTypeRequest request, CancellationToken ct) =>
        mutations.ExecuteAsync(roomId, async (context, room) => {
            await RequireManager(context, room, ct);
            var name = await ValidateName(context, roomId, null, request.Name, ct);
            var type = new RoomEventType { RoomId = roomId, Name = name, NormalizedName = name.ToUpperInvariant(), Color = request.Color.ToUpperInvariant() };
            context.RoomEventTypes.Add(type);
            await context.SaveChangesAsync(ct);
            return Map(type);
        }, ct);
    public Task<EventTypeResponse> UpdateAsync(int roomId, int id, UpdateEventTypeRequest request, CancellationToken ct) =>
        mutations.ExecuteAsync(roomId, async (context, room) => {
            await RequireManager(context, room, ct);
            var type = await context.RoomEventTypes.SingleOrDefaultAsync(t => t.RoomId == roomId && t.Id == id, ct)
                ?? throw new ApiException(404, "event_type_not_found", "Event type not found.");
            if (!type.RowVersion.AsSpan().SequenceEqual(request.RowVersion))
                throw new ApiException(409, "concurrency_conflict", "Event type changed. Reload and retry.");
            if (request.Name != null) {
                type.Name = await ValidateName(context, roomId, id, request.Name, ct);
                type.NormalizedName = type.Name.ToUpperInvariant();
            }
            if (request.Color != null) type.Color = request.Color.ToUpperInvariant();
            if (request.IsArchived is {} archived) type.IsArchived = archived;
            context.Entry(type).Property(t => t.UpdatedAtUtc).IsModified = true;
            await context.SaveChangesAsync(ct);
            return Map(type);
        }, ct);
    private async Task RequireManager(AppDbContext context, Room room, CancellationToken ct) {
        var roomAccess = new RoomAccessService(context, actor, clock);
        await roomAccess.RequireManagerAsync(room.Id, ct);
        roomAccess.RequireActive(room);
    }
    private static async Task<string> ValidateName(AppDbContext context, int roomId, int? id, string value, CancellationToken ct) {
        var name = value.Trim();
        if (name.Length == 0) throw ApiException.Validation(new Dictionary<string, string[]> { ["name"] = ["Name cannot be blank."] });
        var normalized = name.ToUpperInvariant();
        if (await context.RoomEventTypes.AnyAsync(t => t.RoomId == roomId && t.Id != id && t.NormalizedName == normalized, ct))
            throw new ApiException(409, "event_type_name_taken", "This room already has an event type with this name.");
        return name;
    }
    private static EventTypeResponse Map(RoomEventType t) => new(t.Id, t.RoomId, t.Name, t.Color, t.IsArchived, t.RowVersion);
}
