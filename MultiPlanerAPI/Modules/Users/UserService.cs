using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerSharedModels.Contracts.Auth;
using MultiPlanerSharedModels.Contracts.Users;

namespace MultiPlanerAPI.Modules.Users;

public sealed class UserService(
    AppDbContext db, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    CurrentActor actor, GuestSessionService guests, IHttpContextAccessor accessor)
{
    public async Task<SessionResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var errors = LocaleValidation.GetErrors(request.DisplayName, request.CountryCode, request.TimeZoneId);
        if (errors.Count != 0) throw ApiException.Validation(errors);
        cancellationToken.ThrowIfCancellationRequested();
        var email = request.Email.Trim();
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            DisplayName = request.DisplayName.Trim(),
            CountryCode = request.CountryCode,
            TimeZoneId = request.TimeZoneId
        };
        try
        {
            EnsureSuccess(await users.CreateAsync(user, request.Password));
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ApiException(409, "email_already_registered", "An account with this email already exists.");
        }
        return ToResponse(user);
    }

    public async Task<SessionResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedEmail = users.NormalizeEmail(request.Email.Trim());
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is null || !(await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded)
            throw new ApiException(401, "invalid_credentials", "Invalid email or password, or account temporarily locked.");

        // Clear the browser's guest identity on successful account login. Guest
        // contributions are not automatically transferred to the account.
        var context = accessor.HttpContext!;
        if (actor.GuestSessionId is { } guestId)
            await guests.RevokeAsync(guestId, cancellationToken);
        await context.SignOutAsync(SessionAuthentication.GuestScheme);
        await signIn.SignInAsync(user, new AuthenticationProperties { IsPersistent = true, AllowRefresh = false });
        return ToResponse(user);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        if (actor.UserId is { } userId)
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
            EnsureSuccess(await users.UpdateSecurityStampAsync(user));
        }
        if (actor.GuestSessionId is { } guestId)
            await guests.RevokeAsync(guestId, cancellationToken);
        await signIn.SignOutAsync();
        await accessor.HttpContext!.SignOutAsync(SessionAuthentication.GuestScheme);
    }

    public async Task<SessionResponse> GetCurrentAsync(CancellationToken cancellationToken)
    {
        if (actor.UserId is { } userId)
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is not null) return ToResponse(user);
        }
        if (actor.GuestSessionId is { } guestId)
        {
            var guest = await db.GuestSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == guestId, cancellationToken);
            if (guest is not null)
                return new SessionResponse("guest", null, guest.Id, guest.DisplayName,
                    null, null, null, null, null, guest.ExpiresAtUtc);
        }
        throw new ApiException(401, "invalid_session", "The session is no longer valid.");
    }

    public async Task<SessionResponse> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var errors = LocaleValidation.GetErrors(request.DisplayName, request.CountryCode, request.TimeZoneId);
        if (errors.Count != 0) throw ApiException.Validation(errors);
        var userId = actor.RequireUserId();
        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        if (request.DisplayName is not null) user.DisplayName = request.DisplayName.Trim();
        if (request.CountryCode is not null) user.CountryCode = request.CountryCode;
        if (request.TimeZoneId is not null) user.TimeZoneId = request.TimeZoneId;
        if (request.AvatarUrl is not null) user.AvatarUrl = EmptyToNull(request.AvatarUrl);
        if (request.ContactDetails is not null) user.ContactDetails = EmptyToNull(request.ContactDetails.Trim());
        EnsureSuccess(await users.UpdateAsync(user));
        return ToResponse(user);
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private static SessionResponse ToResponse(ApplicationUser user) =>
        new("user", user.Id, null, user.DisplayName, user.Email, user.CountryCode,
            user.TimeZoneId, user.AvatarUrl, user.ContactDetails, null);

    private static void EnsureSuccess(IdentityResult result)
    {
        if (result.Succeeded) return;
        if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
            throw new ApiException(409, "email_already_registered", "An account with this email already exists.");
        if (result.Errors.Any(e => e.Code == "ConcurrencyFailure"))
            throw new ApiException(409, "concurrency_conflict", "The account changed. Reload it and retry.");
        throw ApiException.Validation(result.Errors.GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
    }
}
