using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MultiPlanerSharedModels.Contracts.Auth;

namespace MultiPlanerAPI.Modules.Users;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(UserService users, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf"), AllowAnonymous]
    [ProducesResponseType<CsrfResponse>(StatusCodes.Status200OK)]
    public ActionResult<CsrfResponse> Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return new CsrfResponse(tokens.RequestToken!, "X-CSRF-TOKEN");
    }

    [HttpPost("register"), AllowAnonymous]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SessionResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = await users.RegisterAsync(request, cancellationToken);
        return Created("/api/me", user);
    }

    [HttpPost("login"), AllowAnonymous]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SessionResponse>> Login(LoginRequest request, CancellationToken cancellationToken) =>
        Ok(await users.LoginAsync(request, cancellationToken));

    [HttpPost("logout"), Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await users.LogoutAsync(cancellationToken);
        return NoContent();
    }
}
