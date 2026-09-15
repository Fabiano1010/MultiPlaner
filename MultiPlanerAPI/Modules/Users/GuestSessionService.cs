using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;

namespace MultiPlanerAPI.Modules.Users;

// Intentionally has no HTTP creation endpoint. The invitation module will call this
// only after validating an invitation, with membership saved in the same transaction.
public sealed class GuestSessionService(AppDbContext db, TimeProvider clock, IHttpContextAccessor accessor)
{
    public async Task<GuestSession> CreateAsync(string displayName, CancellationToken cancellationToken = default)
    {
        var errors = LocaleValidation.GetErrors(displayName, null, null);
        if (errors.Count != 0) throw ApiException.Validation(errors);
        var session = new GuestSession
        {
            DisplayName = displayName.Trim(),
            ExpiresAtUtc = clock.GetUtcNow().AddDays(30)
        };
        db.GuestSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task SignInAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await db.GuestSessions.AsNoTracking().SingleOrDefaultAsync(
            s => s.Id == sessionId && s.RevokedAtUtc == null && s.ExpiresAtUtc > clock.GetUtcNow(), cancellationToken)
            ?? throw new ApiException(401, "invalid_session", "The guest session is no longer valid.");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, $"guest:{session.Id}"),
            new Claim(ClaimTypes.Name, session.DisplayName),
            new Claim(SessionAuthentication.GuestIdClaim, session.Id.ToString())
        ], SessionAuthentication.GuestScheme));
        var context = accessor.HttpContext ?? throw new InvalidOperationException("An HTTP context is required.");
        await context.SignInAsync(SessionAuthentication.GuestScheme, principal, new AuthenticationProperties
        {
            IsPersistent = true,
            AllowRefresh = false,
            ExpiresUtc = session.ExpiresAtUtc
        });
    }

    public async Task RevokeAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        await db.GuestSessions.Where(s => s.Id == sessionId && s.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update
                .SetProperty(s => s.RevokedAtUtc, now)
                .SetProperty(s => s.UpdatedAtUtc, now), cancellationToken);
    }
}
