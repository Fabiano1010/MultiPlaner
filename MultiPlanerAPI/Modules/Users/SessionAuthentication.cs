using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Models;
using MultiPlanerSharedModels.Contracts.Auth;

namespace MultiPlanerAPI.Modules.Users;

public static class SessionAuthentication
{
    public const string Scheme = "Session";
    public const string GuestScheme = "GuestSession";
    public const string RegisteredUserPolicy = "RegisteredUser";
    public const string UserCookie = "__Host-MultiPlaner.User";
    public const string GuestCookie = "__Host-MultiPlaner.Guest";
    public const string GuestIdClaim = "guest_session_id";

    public static IServiceCollection AddUserSessions(this IServiceCollection services)
    {
        ConfigureIdentity(services);
        ConfigureAuthentication(services);
        ConfigureAuthorization(services);

        services.AddSingleton<SessionValidationCache>();
        services.AddScoped<UserCookieEvents>();
        services.AddScoped<GuestCookieEvents>();
        services.AddScoped<GuestSessionService>();
        services.AddScoped<CurrentActor>();
        services.AddScoped<UserService>();
        services.AddScoped<IPasswordResetEmailSender, SmtpPasswordResetEmailSender>();
        return services;
    }

    private static void ConfigureIdentity(IServiceCollection services)
    {
        services.AddIdentity<ApplicationUser, IdentityRole<int>>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters = null!;
                options.Password.RequiredLength = PasswordLimits.MinLength;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromHours(1));

        services.ConfigureApplicationCookie(options =>
        {
            ConfigureCookie(options, UserCookie);
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.EventsType = typeof(UserCookieEvents);
        });
    }

    private static void ConfigureAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = Scheme;
                options.DefaultAuthenticateScheme = Scheme;
                options.DefaultChallengeScheme = Scheme;
                options.DefaultForbidScheme = Scheme;
            })
            .AddPolicyScheme(Scheme, Scheme, options =>
                options.ForwardDefaultSelector = context => context.Request.Cookies.ContainsKey(UserCookie)
                    ? IdentityConstants.ApplicationScheme
                    : GuestScheme)
            .AddCookie(GuestScheme, options =>
            {
                ConfigureCookie(options, GuestCookie);
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.EventsType = typeof(GuestCookieEvents);
            });

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<TimeProvider>((options, clock) => options.TimeProvider = clock);
        services.AddOptions<CookieAuthenticationOptions>(GuestScheme)
            .Configure<TimeProvider>((options, clock) => options.TimeProvider = clock);
    }

    private static void ConfigureAuthorization(IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().Build())
            .AddPolicy(RegisteredUserPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => !context.User.HasClaim(c => c.Type == GuestIdClaim)));
    }

    private static void ConfigureCookie(CookieAuthenticationOptions options, string name)
    {
        options.Cookie.Name = name;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.SlidingExpiration = false;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }
}

/// <summary>
/// Validates user cookies against a cached SecurityStamp without regenerating ClaimsPrincipal
/// or renewing/re-encrypting the cookie on every request.
/// </summary>
public sealed class UserCookieEvents(
    AppDbContext db,
    SessionValidationCache cache,
    IOptions<IdentityOptions> identityOptions) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var claimsIdentity = identityOptions.Value.ClaimsIdentity;
        var userIdValue = context.Principal?.FindFirst(claimsIdentity.UserIdClaimType)?.Value;
        var cookieStamp = context.Principal?.FindFirst(claimsIdentity.SecurityStampClaimType)?.Value;

        if (int.TryParse(userIdValue, out var userId) && !string.IsNullOrEmpty(cookieStamp))
        {
            if (!cache.TryGetSecurityStamp(userId, out var currentStamp))
            {
                currentStamp = await db.Users.AsNoTracking()
                    .Where(u => u.Id == userId)
                    .Select(u => u.SecurityStamp)
                    .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                cache.SetSecurityStamp(userId, currentStamp);
            }

            if (!string.IsNullOrEmpty(currentStamp) &&
                string.Equals(currentStamp, cookieStamp, StringComparison.Ordinal))
            {
                return;
            }
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}

public sealed class GuestCookieEvents(
    AppDbContext db,
    TimeProvider clock,
    SessionValidationCache cache) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var claim = context.Principal?.FindFirst(SessionAuthentication.GuestIdClaim)?.Value;
        if (Guid.TryParse(claim, out var id))
        {
            if (!cache.TryGetGuestExpiry(id, out var expiresAtUtc))
            {
                expiresAtUtc = await db.GuestSessions.AsNoTracking()
                    .Where(s => s.Id == id && s.RevokedAtUtc == null)
                    .Select(s => (DateTimeOffset?)s.ExpiresAtUtc)
                    .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                cache.SetGuestExpiry(id, expiresAtUtc);
            }

            if (expiresAtUtc is { } expiry && expiry > clock.GetUtcNow())
            {
                return;
            }
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(SessionAuthentication.GuestScheme);
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}