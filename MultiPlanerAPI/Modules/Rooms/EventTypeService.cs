using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerSharedModels.Contracts.Events;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class EventTypeService(
    AppDbContext db,
    RoomAccessService access,
    RoomMutationService mutations)
{
    private static readonly string[] DefaultTypeNames =
        ["Meeting", "Training", "Social", "Business trip", "Leave", "Shift"];

    public static void AddDefaults(AppDbContext db, int roomId)
    {
        db.RoomEventTypes.AddRange(DefaultTypeNames.Select(name =>
            new RoomEventType { RoomId = roomId, Name = name, NormalizedName = name.ToUpperInvariant() }));
    }

    public async Task<IReadOnlyList<EventTypeResponse>> ListAsync(int roomId, CancellationToken ct)
    {
        var (room, _) = await access.RequireMemberAsync(roomId, ct);
        access.RequireReadable(room);
        return await db.RoomEventTypes.AsNoTracking().Where(t => t.RoomId == roomId).OrderBy(t => t.Id)
            .Select(t => new EventTypeResponse(t.Id, t.RoomId, t.Name, t.Color, t.IsArchived, t.RowVersion))
            .ToArrayAsync(ct);
    }

    public Task<EventTypeResponse> CreateAsync(int roomId, CreateEventTypeRequest request, CancellationToken ct)
    {
        return mutations.ExecuteAsManagerAsync(roomId, async (context, _, _) =>
        {
            var name = await ValidateNameAsync(context, roomId, null, request.Name, ct);
            var type = new RoomEventType
            {
                RoomId = roomId, Name = name, NormalizedName = name.ToUpperInvariant(),
                Color = request.Color.ToUpperInvariant()
            };
            context.RoomEventTypes.Add(type);
            await context.SaveChangesAsync(ct);
            return Map(type);
        }, ct);
    }

    public Task<EventTypeResponse> UpdateAsync(int roomId, int id, UpdateEventTypeRequest request, CancellationToken ct)
    {
        return mutations.ExecuteAsManagerAsync(roomId, async (context, _, _) =>
        {
            var type = await context.RoomEventTypes.SingleOrDefaultAsync(t => t.RoomId == roomId && t.Id == id, ct)
                       ?? throw new ApiException(404, "event_type_not_found", "Event type not found.");
            ApiException.RequireVersion(type.RowVersion, request.RowVersion, "Event type changed. Reload and retry.");

            if (request.Name is not null)
            {
                type.Name = await ValidateNameAsync(context, roomId, id, request.Name, ct);
                type.NormalizedName = type.Name.ToUpperInvariant();
            }

            if (request.Color is not null)
            {
                type.Color = request.Color.ToUpperInvariant();
            }

            if (request.IsArchived is { } archived)
            {
                type.IsArchived = archived;
            }

            context.Entry(type).Property(t => t.UpdatedAtUtc).IsModified = true;
            await context.SaveChangesAsync(ct);
            return Map(type);
        }, ct);
    }

    private static async Task<string> ValidateNameAsync(AppDbContext context, int roomId, int? id, string value,
        CancellationToken ct)
    {
        var name = value.Trim();
        if (name.Length == 0)
        {
            throw ApiException.Validation("name", "Name cannot be blank.");
        }

        var normalized = name.ToUpperInvariant();
        if (await context.RoomEventTypes.AnyAsync(
                t => t.RoomId == roomId && t.Id != id && t.NormalizedName == normalized, ct))
        {
            throw new ApiException(409, "event_type_name_taken", "This room already has an event type with this name.");
        }

        return name;
    }

    private static EventTypeResponse Map(RoomEventType t)
    {
        return new EventTypeResponse(t.Id, t.RoomId, t.Name, t.Color, t.IsArchived, t.RowVersion);
    }
}