using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiPlanerSharedModels.Contracts.Auth;
using MultiPlanerSharedModels.Contracts.Users;

namespace MultiPlanerAPI.Modules.Users;

[ApiController]
[Route("api/me")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MeController(UserService users) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SessionResponse>> Get(CancellationToken cancellationToken) =>
        Ok(await users.GetCurrentAsync(cancellationToken));

    [HttpPatch, Authorize(Policy = SessionAuthentication.RegisteredUserPolicy)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SessionResponse>> Update(UpdateProfileRequest request, CancellationToken cancellationToken) =>
        Ok(await users.UpdateProfileAsync(request, cancellationToken));
}
