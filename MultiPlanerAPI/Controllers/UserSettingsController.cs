using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Users;
namespace MultiPlanerAPI.Controllers;
[ApiController, Route("api/me"), Authorize(Policy = SessionAuthentication.RegisteredUserPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UserSettingsController(UserSettingsService settings, AvatarService avatars, UserService users) : ControllerBase
{
    [HttpGet("theme")] public async Task<ActionResult<ThemeResponse>> Theme(CancellationToken ct) => Ok(await settings.GetAsync(ct));
    [HttpPut("theme")] public async Task<ActionResult<ThemeResponse>> SaveTheme(SaveThemeRequest request, CancellationToken ct) => Ok(await settings.SaveAsync(request.Palette, request.RowVersion, ct));
    [HttpDelete("theme")] public async Task<ActionResult<ThemeResponse>> ResetTheme([FromBody] ResetThemeRequest request, CancellationToken ct) => Ok(await settings.SaveAsync(new(), request.RowVersion, ct));
    [HttpPut("avatar"), RequestSizeLimit(AvatarService.MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> Avatar(IFormFile file, CancellationToken ct) { await avatars.SaveAsync(file, ct); return NoContent(); }
    [HttpDelete("avatar")] public async Task<IActionResult> RemoveAvatar(CancellationToken ct) { await avatars.DeleteAsync(ct); return NoContent(); }
    [HttpPost("change-password"), EnableRateLimiting("PasswordChange")]
    public async Task<IActionResult> Password(ChangePasswordRequest request, CancellationToken ct) { await users.ChangePasswordAsync(request, ct); return NoContent(); }
}
[ApiController, Route("api/users/{userId:int}/avatar"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UserAvatarController(AvatarService avatars) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Get(int userId, CancellationToken ct) {
        var (stream, type) = await avatars.OpenAsync(userId, ct);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stream, type);
    }
}
