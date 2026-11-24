using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class UserFolderService(
    AppDbContext db,
    CurrentActor actor,
    RoomMutationService mutations,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<UserFolderResponse>> ListAsync(CancellationToken ct)
    {
        var userId = actor.RequireUserId();
        var folders = await db.UserFolders.AsNoTracking().Where(f => f.OwnerUserId == userId).OrderBy(f => f.Name).ToListAsync(ct);
        var assignments = await db.UserFolderRooms.AsNoTracking().Where(a => a.OwnerUserId == userId).ToListAsync(ct);
        return folders.Select(f => ToResponse(f, assignments.Where(a => a.FolderId == f.Id).Select(a => a.RoomId).ToArray())).ToArray();
    }

    public async Task<UserFolderResponse> CreateAsync(CreateUserFolderRequest request, CancellationToken ct)
    {
        var folder = new UserFolder { OwnerUserId = actor.RequireUserId(), Name = ValidName(request.Name) };
        db.UserFolders.Add(folder);
        await SaveAsync(ct);
        return ToResponse(folder, []);
    }

    public async Task<UserFolderResponse> GetAsync(int id, CancellationToken ct)
    {
        var folder = await RequireFolderAsync(id, ct);
        var roomIds = await db.UserFolderRooms.AsNoTracking()
            .Where(a => a.FolderId == id && a.OwnerUserId == folder.OwnerUserId)
            .Select(a => a.RoomId).ToArrayAsync(ct);
        return ToResponse(folder, roomIds);
    }

    public async Task<UserFolderResponse> UpdateAsync(int id, UpdateUserFolderRequest request, CancellationToken ct)
    {
        var folder = await RequireFolderAsync(id, ct);
        if (!folder.RowVersion.AsSpan().SequenceEqual(request.RowVersion))
        {
            throw new ApiException(409, "concurrency_conflict", "Folder changed. Reload and retry.");
        }
        if (request.Name is not null) { folder.Name = ValidName(request.Name); }
        if (request.IsFavourite is { } favourite) { folder.IsFavourite = favourite; }

        await SaveAsync(ct);
        var rooms = await db.UserFolderRooms.Where(a => a.FolderId == id && a.OwnerUserId == folder.OwnerUserId)
            .Select(a => a.RoomId).ToArrayAsync(ct);
        return ToResponse(folder, rooms);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var folder = await RequireFolderAsync(id, ct);
        var assignments = await db.UserFolderRooms.Where(a => a.FolderId == id && a.OwnerUserId == folder.OwnerUserId).ToListAsync(ct);
        db.UserFolderRooms.RemoveRange(assignments);
        db.UserFolders.Remove(folder);
        await db.SaveChangesAsync(ct);
    }

    public Task AssignAsync(int folderId, int roomId, CancellationToken ct)
    {
        return mutations.ExecuteAsync(roomId, async (operationDb, room) =>
        {
            var userId = actor.RequireUserId();
            var roomAccess = new RoomAccessService(operationDb, actor, clock);
            await roomAccess.RequireMemberAsync(room, ct);
            roomAccess.RequireReadable(room);

            if (!await operationDb.UserFolders.AsNoTracking().AnyAsync(f => f.Id == folderId && f.OwnerUserId == userId, ct))
            {
                throw new ApiException(404, "folder_not_found", "Folder not found.");
            }

            var assignment = await operationDb.UserFolderRooms.SingleOrDefaultAsync(
                a => a.OwnerUserId == userId && a.RoomId == roomId, ct);
            if (assignment is null)
            {
                operationDb.UserFolderRooms.Add(new UserFolderRoom { OwnerUserId = userId, RoomId = roomId, FolderId = folderId });
            }
            else
            {
                assignment.FolderId = folderId;
            }

            try
            {
                await operationDb.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
            {
                throw new ApiException(404, "folder_not_found", "Folder not found.");
            }
            return true;
        }, ct);
    }

    public async Task UnassignAsync(int folderId, int roomId, CancellationToken ct)
    {
        var folder = await RequireFolderAsync(folderId, ct);
        var assignment = await db.UserFolderRooms.SingleOrDefaultAsync(
            a => a.OwnerUserId == folder.OwnerUserId && a.RoomId == roomId && a.FolderId == folderId, ct);
        if (assignment is null)
        {
            return;
        }
        db.UserFolderRooms.Remove(assignment);
        await db.SaveChangesAsync(ct);
    }

    private async Task<UserFolder> RequireFolderAsync(int id, CancellationToken ct) =>
        await db.UserFolders.SingleOrDefaultAsync(f => f.Id == id && f.OwnerUserId == actor.RequireUserId(), ct)
        ?? throw new ApiException(404, "folder_not_found", "Folder not found.");

    private static string ValidName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is < 2 or > 128)
        {
            throw ApiException.Validation(new Dictionary<string, string[]> { ["name"] = ["Folder name must contain 2–128 characters."] });
        }
        return trimmed;
    }

    private static UserFolderResponse ToResponse(UserFolder folder, IReadOnlyList<int> roomIds) =>
        new(folder.Id, folder.Name, folder.IsFavourite, folder.RowVersion, roomIds);

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ApiException(409, "folder_name_exists", "A folder with this name already exists.");
        }
    }
}
