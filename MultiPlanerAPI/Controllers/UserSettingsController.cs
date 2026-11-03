using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Users;

namespace MultiPlanerAPI.Controllers;

[ApiController]
[Route("api/me")]
[Authorize(Policy = SessionAuthentication.RegisteredUserPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UserSettingsController(UserSettingsService settings, AvatarService avatars, UserService users)
    : ControllerBase
{
    [HttpGet("theme")]
    public async Task<ActionResult<ThemeResponse>> Theme(CancellationToken ct)
    {
        return Ok(await settings.GetAsync(ct));
    }

    [HttpPut("theme")]
    public async Task<ActionResult<ThemeResponse>> Apply(ApplyThemeRequest request, CancellationToken ct)
    {
        return Ok(await settings.ApplyAsync(request, ct));
    }

    [HttpGet("themes")]
    public async Task<ActionResult<IReadOnlyList<SavedThemeResponse>>> Themes(CancellationToken ct)
    {
        return Ok(await settings.ListAsync(ct));
    }

    [HttpPost("themes")]
    public async Task<ActionResult<SavedThemeResponse>> CreateTheme(SaveThemeRequest request, CancellationToken ct)
    {
        var value = await settings.SaveAsync(null, request, ct);
        return Created($"/api/me/themes/{value.Id}", value);
    }

    [HttpPut("themes/{themeId:int}")]
    public async Task<ActionResult<SavedThemeResponse>> SaveTheme(int themeId, SaveThemeRequest request,
        CancellationToken ct)
    {
        return Ok(await settings.SaveAsync(themeId, request, ct));
    }

    [HttpDelete("themes/{themeId:int}")]
    public async Task<IActionResult> DeleteTheme(int themeId, [FromBody] ResetThemeRequest request,
        CancellationToken ct)
    {
        await settings.DeleteAsync(themeId, request.RowVersion, ct);
        return NoContent();
    }

    [HttpPut("avatar")]
    [RequestSizeLimit(AvatarService.MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> Avatar(IFormFile file, CancellationToken ct)
    {
        await avatars.SaveAsync(file, ct);
        return NoContent();
    }

    [HttpDelete("avatar")]
    public async Task<IActionResult> RemoveAvatar(CancellationToken ct)
    {
        await avatars.DeleteAsync(ct);
        return NoContent();
    }

    [HttpPost("change-password")]
    [EnableRateLimiting("PasswordChange")]
    public async Task<IActionResult> Password(ChangePasswordRequest request, CancellationToken ct)
    {
        await users.ChangePasswordAsync(request, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/users/{userId:int}/avatar")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UserAvatarController(AvatarService avatars) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(int userId, CancellationToken ct)
    {
        var (stream, type) = await avatars.OpenAsync(userId, ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stream, type);
    }
}