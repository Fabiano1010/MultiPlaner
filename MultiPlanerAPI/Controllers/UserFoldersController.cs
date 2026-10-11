using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiPlanerAPI.Modules.Rooms;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.Controllers;

[ApiController]
[Route("api/me/folders")]
[Authorize(Policy = SessionAuthentication.RegisteredUserPolicy)]
public sealed class UserFoldersController(UserFolderService folders) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UserFolderResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserFolderResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await folders.ListAsync(cancellationToken));

    [HttpGet("{folderId:int}")]
    [ProducesResponseType<UserFolderResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserFolderResponse>> Get(int folderId, CancellationToken cancellationToken) =>
        Ok(await folders.GetAsync(folderId, cancellationToken));

    [HttpPost]
    [ProducesResponseType<UserFolderResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<UserFolderResponse>> Create(CreateUserFolderRequest request,
        CancellationToken cancellationToken)
    {
        var folder = await folders.CreateAsync(request, cancellationToken);
        return Created($"/api/me/folders/{folder.Id}", folder);
    }

    [HttpPatch("{folderId:int}")]
    [ProducesResponseType<UserFolderResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserFolderResponse>> Update(int folderId, UpdateUserFolderRequest request,
        CancellationToken cancellationToken) => Ok(await folders.UpdateAsync(folderId, request, cancellationToken));

    [HttpDelete("{folderId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int folderId, CancellationToken cancellationToken)
    {
        await folders.DeleteAsync(folderId, cancellationToken);
        return NoContent();
    }

    [HttpPut("{folderId:int}/rooms/{roomId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Assign(int folderId, int roomId, CancellationToken cancellationToken)
    {
        await folders.AssignAsync(folderId, roomId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{folderId:int}/rooms/{roomId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unassign(int folderId, int roomId, CancellationToken cancellationToken)
    {
        await folders.UnassignAsync(folderId, roomId, cancellationToken);
        return NoContent();
    }
}
