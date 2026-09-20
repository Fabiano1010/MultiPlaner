using System.Security.Claims;
using MultiPlanerAPI.Infrastructure;

namespace MultiPlanerAPI.Modules.Users;

public sealed class CurrentActor(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal Principal => accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public int? UserId => Principal.Identity?.IsAuthenticated == true &&
                          !Principal.HasClaim(c => c.Type == SessionAuthentication.GuestIdClaim) &&
                          int.TryParse(Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public Guid? GuestSessionId => Principal.Identity?.IsAuthenticated == true &&
                                  Guid.TryParse(Principal.FindFirstValue(SessionAuthentication.GuestIdClaim), out var id) ? id : null;

    public int RequireUserId() => UserId ??
        throw new ApiException(StatusCodes.Status403Forbidden, "registered_user_required", "A registered account is required.");
}
