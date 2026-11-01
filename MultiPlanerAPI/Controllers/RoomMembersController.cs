using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiPlanerAPI.Modules.Rooms;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Rooms;
namespace MultiPlanerAPI.Controllers;
[ApiController, Route("api/rooms/{roomId:int}/members")]
[Authorize(Policy = SessionAuthentication.RegisteredUserPolicy)]
public sealed class RoomMembersController(MemberManagementService members) : ControllerBase
{
    [HttpPatch("{memberId:int}/role")]
    public async Task<IActionResult> Role(int roomId, int memberId, ChangeMemberRoleRequest request, CancellationToken ct)
    { await members.ChangeRoleAsync(roomId, memberId, request, ct); return NoContent(); }
    [HttpDelete("{memberId:int}")]
    public async Task<IActionResult> Remove(int roomId, int memberId, [FromBody] RemoveMemberRequest request, CancellationToken ct)
    { await members.RemoveAsync(roomId, memberId, request, ct); return NoContent(); }
}
