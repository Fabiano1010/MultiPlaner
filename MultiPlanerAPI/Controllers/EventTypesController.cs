using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiPlanerAPI.Modules.Rooms;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Events;
namespace MultiPlanerAPI.Controllers;
[ApiController, Route("api/rooms/{roomId:int}/event-types"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EventTypesController(EventTypeService types) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<IReadOnlyList<EventTypeResponse>>> List(int roomId, CancellationToken ct) => Ok(await types.ListAsync(roomId, ct));
    [HttpPost, Authorize(Policy = SessionAuthentication.RegisteredUserPolicy)]
    public async Task<ActionResult<EventTypeResponse>> Create(int roomId, CreateEventTypeRequest request, CancellationToken ct) {
        var result = await types.CreateAsync(roomId, request, ct);
        return Created($"/api/rooms/{roomId}/event-types/{result.Id}", result);
    }
    [HttpPatch("{typeId:int}"), Authorize(Policy = SessionAuthentication.RegisteredUserPolicy)]
    public async Task<ActionResult<EventTypeResponse>> Update(int roomId, int typeId, UpdateEventTypeRequest request, CancellationToken ct) => Ok(await types.UpdateAsync(roomId, typeId, request, ct));
}
