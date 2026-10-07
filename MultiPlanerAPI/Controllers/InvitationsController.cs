using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiPlanerAPI.Modules.Rooms;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.Controllers;

[ApiController]
[Route("api/invitations")]
[AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InvitationsController(InvitationService invitations) : ControllerBase
{
    [HttpPost("preview")]
    [ProducesResponseType<InvitationPreviewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<InvitationPreviewResponse>> Preview(
        PreviewInvitationRequest request, CancellationToken cancellationToken) =>
        Ok(await invitations.PreviewAsync(request.Token, cancellationToken));

    [HttpPost("join")]
    [ProducesResponseType<RoomResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RoomResponse>> Join(
        JoinByTokenRequest request, CancellationToken cancellationToken) =>
        Ok(await invitations.JoinAsync(request.Token,
            new JoinInvitationRequest { DisplayName = request.DisplayName }, cancellationToken));

    [HttpGet("{token}")]
    [ProducesResponseType<InvitationPreviewResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<InvitationPreviewResponse>> PreviewByToken(
        string token, CancellationToken cancellationToken) =>
        Ok(await invitations.PreviewAsync(token, cancellationToken));

    [HttpPost("{token}/join")]
    [ProducesResponseType<RoomResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RoomResponse>> JoinByToken(
        string token, JoinInvitationRequest request, CancellationToken cancellationToken) =>
        Ok(await invitations.JoinAsync(token, request, cancellationToken));
}
