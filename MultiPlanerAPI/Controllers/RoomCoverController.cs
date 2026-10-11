using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiPlanerAPI.Modules.Rooms;

namespace MultiPlanerAPI.Controllers;

[ApiController]
[Route("api/rooms/{roomId:int}/cover")]
[Authorize]
public sealed class RoomCoverController(RoomCoverService covers) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(int roomId, CancellationToken cancellationToken)
    {
        var (stream, contentType) = await covers.OpenAsync(roomId, cancellationToken);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stream, contentType);
    }

    [HttpPut]
    [RequestSizeLimit(10 * 1024 * 1024 + 1024 * 1024)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Put(int roomId, IFormFile file, CancellationToken cancellationToken)
    {
        await covers.SaveAsync(roomId, file, cancellationToken);
        return NoContent();
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int roomId, CancellationToken cancellationToken)
    {
        await covers.DeleteAsync(roomId, cancellationToken);
        return NoContent();
    }
}
