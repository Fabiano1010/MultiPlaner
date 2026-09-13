using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Models;

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
        services.AddIdentity<ApplicationUser, IdentityRole<int>>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.User.AllowedUserNameCharacters = null!;
            options.Password.RequiredLength = 12;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            ConfigureCookie(options, UserCookie);
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
        });
        // Logout rotates the stamp; validating on every request rejects copied old cookies.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = Scheme;
            options.DefaultAuthenticateScheme = Scheme;
            options.DefaultChallengeScheme = Scheme;
            options.DefaultForbidScheme = Scheme;
        })
        .AddPolicyScheme(Scheme, Scheme, options =>
            options.ForwardDefaultSelector = context => context.Request.Cookies.ContainsKey(UserCookie)
                ? IdentityConstants.ApplicationScheme : GuestScheme)
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
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser().Build())
            .AddPolicy(RegisteredUserPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => !context.User.HasClaim(c => c.Type == GuestIdClaim)));

        services.AddScoped<GuestCookieEvents>();
        services.AddScoped<GuestSessionService>();
        services.AddScoped<CurrentActor>();
        services.AddScoped<UserService>();
        return services;
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

public sealed class GuestCookieEvents(AppDbContext db, TimeProvider clock) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var claim = context.Principal?.FindFirst(SessionAuthentication.GuestIdClaim)?.Value;
        if (Guid.TryParse(claim, out var id) &&
            await db.GuestSessions.AsNoTracking().AnyAsync(
                s => s.Id == id && s.RevokedAtUtc == null && s.ExpiresAtUtc > clock.GetUtcNow(),
                context.HttpContext.RequestAborted))
            return;

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
