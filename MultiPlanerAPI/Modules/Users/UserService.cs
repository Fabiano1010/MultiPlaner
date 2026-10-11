using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Models;
using MultiPlanerSharedModels.Contracts.Auth;
using MultiPlanerSharedModels.Contracts.Users;

namespace MultiPlanerAPI.Modules.Users;

public sealed class UserService(
    AppDbContext db, UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    CurrentActor actor, GuestSessionService guests, IHttpContextAccessor accessor,
    IPasswordResetEmailSender emailSender, IConfiguration configuration, TimeProvider clock,
    ILogger<UserService> logger)
{
    public async Task<SessionResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var errors = LocaleValidation.GetErrors(request.DisplayName, request.CountryCode, request.TimeZoneId);
        if (errors.Count != 0)
        {
            throw ApiException.Validation(errors);
        }

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
        {
            throw new ApiException(401, "invalid_credentials", "Invalid email or password, or account temporarily locked.");
        }

        await ClearGuestIdentityAsync(cancellationToken);
        await signIn.SignInAsync(user, new AuthenticationProperties
        {
            IsPersistent = request.RememberMe,
            AllowRefresh = false,
            ExpiresUtc = clock.GetUtcNow().AddDays(14)
        });
        return ToResponse(user);
    }

    public async Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = users.NormalizeEmail(request.Email.Trim());
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is null) return;

        try
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var publicBase = configuration["PasswordReset:PublicWebBaseUrl"];
            if (!Uri.TryCreate(publicBase, UriKind.Absolute, out var baseUri) ||
                baseUri.Scheme != Uri.UriSchemeHttps || baseUri.UserInfo.Length != 0 ||
                baseUri.Query.Length != 0 || baseUri.Fragment.Length != 0)
                throw new InvalidOperationException("Configure PasswordReset:PublicWebBaseUrl as the HTTPS web application address.");
            var link = $"{baseUri.GetLeftPart(UriPartial.Authority)}{baseUri.AbsolutePath.TrimEnd('/')}/reset-password" +
                       $"?email={Uri.EscapeDataString(user.Email!)}&token={encoded}";
            await emailSender.SendAsync(user.Email!, link, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Password reset delivery failed.");
        }
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = users.NormalizeEmail(request.Email.Trim());
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token)); }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        { throw new ApiException(400, "invalid_reset_token", "The reset link is invalid or expired."); }
        if (user is null) throw new ApiException(400, "invalid_reset_token", "The reset link is invalid or expired.");
        var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
        if (result.Succeeded) return;
        if (result.Errors.Any(e => e.Code == "InvalidToken"))
            throw new ApiException(400, "invalid_reset_token", "The reset link is invalid or expired.");
        EnsureSuccess(result);
    }

    /// <summary>Removes a browser's guest identity without transferring guest contributions to the account.</summary>
    private async Task ClearGuestIdentityAsync(CancellationToken cancellationToken)
    {
        if (actor.GuestSessionId is { } guestId)
        {
            await guests.RevokeAsync(guestId, cancellationToken);
        }

        await accessor.HttpContext!.SignOutAsync(SessionAuthentication.GuestScheme);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        if (actor.UserId is { } userId)
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
            EnsureSuccess(await users.UpdateSecurityStampAsync(user));
        }
        if (actor.GuestSessionId is { } guestId)
        {
            await guests.RevokeAsync(guestId, cancellationToken);
        }

        await signIn.SignOutAsync();
        await accessor.HttpContext!.SignOutAsync(SessionAuthentication.GuestScheme);
    }

    public async Task<SessionResponse> GetCurrentAsync(CancellationToken cancellationToken)
    {
        if (actor.UserId is { } userId)
        {
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is not null)
            {
                return ToResponse(user);
            }
        }
        if (actor.GuestSessionId is { } guestId)
        {
            var guest = await db.GuestSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == guestId, cancellationToken);
            if (guest is not null)
            {
                return new SessionResponse("guest", null, guest.Id, guest.DisplayName,
                    null, null, null, null, null, guest.ExpiresAtUtc);
            }
        }
        throw new ApiException(401, "invalid_session", "The session is no longer valid.");
    }

    public async Task<SessionResponse> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var errors = LocaleValidation.GetErrors(request.DisplayName, request.CountryCode, request.TimeZoneId);
        if (errors.Count != 0)
        {
            throw ApiException.Validation(errors);
        }

        var userId = actor.RequireUserId();
        var user = await db.Users.SingleAsync(u => u.Id == userId, cancellationToken);
        if (request.DisplayName is not null)
        {
            user.DisplayName = request.DisplayName.Trim();
        }

        if (request.CountryCode is not null)
        {
            user.CountryCode = request.CountryCode;
        }

        if (request.TimeZoneId is not null)
        {
            user.TimeZoneId = request.TimeZoneId;
        }

        if (request.AvatarUrl is not null)
        {
            user.AvatarUrl = EmptyToNull(request.AvatarUrl);
        }

        if (request.ContactDetails is not null)
        {
            user.ContactDetails = EmptyToNull(request.ContactDetails.Trim());
        }

        EnsureSuccess(await users.UpdateAsync(user));
        return ToResponse(user);
    }

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private static SessionResponse ToResponse(ApplicationUser user) =>
        new("user", user.Id, null, user.DisplayName, user.Email, user.CountryCode,
            user.TimeZoneId, user.AvatarUrl, user.ContactDetails, null);

    private static void EnsureSuccess(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
        {
            throw new ApiException(409, "email_already_registered", "An account with this email already exists.");
        }

        if (result.Errors.Any(e => e.Code == "ConcurrencyFailure"))
        {
            throw new ApiException(409, "concurrency_conflict", "The account changed. Reload it and retry.");
        }

        throw ApiException.Validation(result.Errors.GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
    }
}
