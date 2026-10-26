using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiPlanerAPI.Modules.Events;
using MultiPlanerSharedModels.Contracts.Common;
using MultiPlanerSharedModels.Contracts.Events;

namespace MultiPlanerAPI.Controllers;

[ApiController, Authorize]
[Route("api/rooms/{roomId:int}/events")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
public sealed class EventsController(EventService events) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<EventResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<EventResponse>>> List(int roomId,
        [FromQuery] EventListRequest filter, CancellationToken ct) => Ok(await events.ListAsync(roomId, filter, ct));

    [HttpGet("{eventId:int}")]
    [ProducesResponseType<EventResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EventResponse>> Get(int roomId, int eventId, CancellationToken ct) =>
        Ok(await events.GetAsync(roomId, eventId, ct));

    [HttpPost]
    [ProducesResponseType<EventResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<EventResponse>> Create(int roomId, CreateEventRequest request, CancellationToken ct)
    {
        var result = await events.CreateAsync(roomId, request, ct);
        return Created($"/api/rooms/{roomId}/events/{result.Id}", result);
    }

    [HttpPatch("{eventId:int}")]
    [ProducesResponseType<EventResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EventResponse>> Update(int roomId, int eventId,
        UpdateEventRequest request, CancellationToken ct) => Ok(await events.UpdateAsync(roomId, eventId, request, ct));

    [HttpPut("{eventId:int}/pin")]
    [ProducesResponseType<EventResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<EventResponse>> Pin(int roomId, int eventId,
        PinEventRequest request, CancellationToken ct) => Ok(await events.PinAsync(roomId, eventId, request, ct));

    [HttpDelete("{eventId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int roomId, int eventId, [FromBody] EventVersionRequest request, CancellationToken ct)
    {
        await events.DeleteAsync(roomId, eventId, request, ct);
        return NoContent();
    }
}

[ApiController, Authorize]
[Route("api/me/events")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MyEventsController(EventService events) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<EventResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResponse<EventResponse>>> List([FromQuery] EventListRequest filter,
        CancellationToken ct) => Ok(await events.ListAsync(null, filter, ct));
}
