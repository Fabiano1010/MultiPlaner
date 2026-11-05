using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiPlanerAPI.Modules.Polls;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Common;
using MultiPlanerSharedModels.Contracts.Polls;

namespace MultiPlanerAPI.Controllers;

[ApiController]
[Route("api/rooms/{roomId:int}/polls")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PollsController(PollService polls) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = SessionAuthentication.RegisteredUserPolicy)]
    [ProducesResponseType<PollResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PollResponse>> Create(
        int roomId, CreatePollRequest request, CancellationToken cancellationToken)
    {
        var poll = await polls.CreateAsync(roomId, request, cancellationToken);
        return Created($"/api/rooms/{roomId}/polls/{poll.Id}", poll);
    }

    [HttpGet]
    [ProducesResponseType<PagedResponse<PollResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<PollResponse>>> List(
        int roomId, [FromQuery] PageRequest pagination, [FromQuery] string status = "all",
        CancellationToken cancellationToken = default)
    {
        return Ok(await polls.ListAsync(roomId, pagination, status, cancellationToken));
    }

    [HttpGet("{pollId:int}")]
    [ProducesResponseType<PollResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PollResponse>> Get(int roomId, int pollId, CancellationToken cancellationToken)
    {
        return Ok(await polls.GetAsync(roomId, pollId, cancellationToken));
    }

    [HttpPatch("{pollId:int}")]
    [Authorize(Policy = SessionAuthentication.RegisteredUserPolicy)]
    [ProducesResponseType<PollResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PollResponse>> Update(
        int roomId, int pollId, UpdatePollRequest request, CancellationToken cancellationToken)
    {
        return Ok(await polls.UpdateAsync(roomId, pollId, request, cancellationToken));
    }

    [HttpPost("{pollId:int}/close")]
    [Authorize(Policy = SessionAuthentication.RegisteredUserPolicy)]
    [ProducesResponseType<PollResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PollResponse>> Close(int roomId, int pollId, CancellationToken cancellationToken)
    {
        return Ok(await polls.CloseAsync(roomId, pollId, cancellationToken));
    }

    [HttpPut("{pollId:int}/vote")]
    [ProducesResponseType<PollResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PollResponse>> Vote(
        int roomId, int pollId, VotePollRequest request, CancellationToken cancellationToken)
    {
        return Ok(await polls.VoteAsync(roomId, pollId, request, cancellationToken));
    }

    [HttpDelete("{pollId:int}")]
    [Authorize(Policy = SessionAuthentication.RegisteredUserPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int roomId, int pollId, CancellationToken cancellationToken)
    {
        await polls.DeleteAsync(roomId, pollId, cancellationToken);
        return NoContent();
    }
}