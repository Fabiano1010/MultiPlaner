using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiPlanerAPI.Modules.Rooms;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Common;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerAPI.Controllers;

[ApiController]
[Route("api/rooms")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RoomsController(RoomService rooms) : ControllerBase
{
    [HttpPost, Authorize(Policy = SessionAuthentication.RegisteredUserPolicy)]
    [ProducesResponseType<RoomResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<RoomResponse>> Create(CreateRoomRequest request, CancellationToken cancellationToken)
    {
        var room = await rooms.CreateAsync(request, cancellationToken);
        return Created($"/api/rooms/{room.Id}", room);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RoomResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RoomResponse>>> List(
        [FromQuery] string status = "active", CancellationToken cancellationToken = default) =>
        Ok(await rooms.ListAsync(status, cancellationToken));

    [HttpGet("{roomId:int}")]
    [ProducesResponseType<RoomResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RoomResponse>> Get(int roomId, CancellationToken cancellationToken) =>
        Ok(await rooms.GetAsync(roomId, cancellationToken));

    [HttpPatch("{roomId:int}"), Authorize(Policy = SessionAuthentication.RegisteredUserPolicy)]
    [ProducesResponseType<RoomResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RoomResponse>> Update(
        int roomId, UpdateRoomRequest request, CancellationToken cancellationToken) =>
        Ok(await rooms.UpdateAsync(roomId, request, cancellationToken));

    [HttpGet("{roomId:int}/members")]
    [ProducesResponseType<PagedResponse<RoomMemberResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<RoomMemberResponse>>> Members(
        int roomId, [FromQuery] PageRequest pagination, CancellationToken cancellationToken) =>
        Ok(await rooms.ListMembersAsync(roomId, pagination, cancellationToken));

    [HttpGet("{roomId:int}/members/me")]
    [ProducesResponseType<RoomMembershipResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RoomMembershipResponse>> Membership(
        int roomId, CancellationToken cancellationToken) =>
        Ok(await rooms.GetMembershipAsync(roomId, cancellationToken));

    [HttpPatch("{roomId:int}/members/me")]
    [ProducesResponseType<RoomMembershipResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<RoomMembershipResponse>> UpdateMembership(
        int roomId, UpdateRoomMembershipRequest request, CancellationToken cancellationToken) =>
        Ok(await rooms.UpdateMembershipAsync(roomId, request, cancellationToken));

    [HttpPost("{roomId:int}/archive"), Authorize(Policy = SessionAuthentication.RegisteredUserPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(int roomId, CancellationToken cancellationToken)
    {
        await rooms.ArchiveAsync(roomId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{roomId:int}"), Authorize(Policy = SessionAuthentication.RegisteredUserPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int roomId, CancellationToken cancellationToken)
    {
        await rooms.DeleteAsync(roomId, cancellationToken);
        return NoContent();
    }
}
