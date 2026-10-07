using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiPlanerAPI.Modules.Rooms;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.Controllers;

[ApiController]
[Route("api/rooms/{roomId:int}/invitations")]
[Authorize(Policy = SessionAuthentication.RegisteredUserPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RoomInvitationsController(InvitationService invitations) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RoomInvitationResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<RoomInvitationResponse>> Create(
        int roomId, CreateRoomInvitationRequest request, CancellationToken cancellationToken)
    {
        var invitation = await invitations.CreateAsync(roomId, request, cancellationToken);
        return Created(invitation.JoinPath, invitation);
    }

    [HttpDelete("{invitationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Revoke(int roomId, Guid invitationId, CancellationToken cancellationToken)
    {
        await invitations.RevokeAsync(roomId, invitationId, cancellationToken);
        return NoContent();
    }
}
