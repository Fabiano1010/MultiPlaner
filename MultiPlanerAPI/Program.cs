using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Identity;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Infrastructure;
using MultiPlanerAPI.Modules.Rooms;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerAPI.Modules.Events;
using MultiPlanerAPI.Modules.Polls;
using MultiPlanerSharedModels.Contracts.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache(options => options.SizeLimit = 100_000);
builder.Services.AddHybridCache(options =>
{
    options.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
    {
        Expiration = SessionValidationCache.EntryTtl,
        LocalCacheExpiration = SessionValidationCache.EntryTtl
    };
});

var keyPath = builder.Configuration["DataProtection:KeysPath"] ??
              Path.Combine(builder.Environment.ContentRootPath, ".data-protection-keys");
if (OperatingSystem.IsWindows())
{
    Directory.CreateDirectory(keyPath);
}
else if (!Directory.Exists(keyPath))
{
    Directory.CreateDirectory(keyPath,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
}

var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("MultiPlanerAPI")
    .PersistKeysToFileSystem(new DirectoryInfo(keyPath));
var dpCertificates = new List<X509Certificate2>();
var certPath = builder.Configuration["DataProtection:CertificatePath"];
if (!string.IsNullOrWhiteSpace(certPath))
{
    var certPassword = builder.Configuration["DataProtection:CertificatePassword"];
    dataProtection.ProtectKeysWithCertificate(LoadPrivateKeyCert(certPath, certPassword, dpCertificates));

    var extraPaths = builder.Configuration.GetSection("DataProtection:DecryptionCertificatePaths").Get<string[]>() ??
                     [];
    var decryptionCerts = extraPaths
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Select(p => LoadPrivateKeyCert(p, certPassword, dpCertificates))
        .ToArray();
    if (decryptionCerts.Length > 0)
    {
        dataProtection.UnprotectKeysWithAnyCertificate(decryptionCerts);
    }
}
else if (!builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "Configure DataProtection:CertificatePath to encrypt Data Protection keys outside Development.");
}

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("PasswordReset", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
    options.AddPolicy("PasswordChange", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ??
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers(options => options.Filters.Add<CsrfValidationFilter>())
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more fields are invalid.",
            Extensions = { ["code"] = "validation_failed", ["traceId"] = context.HttpContext.TraceIdentifier }
        };
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    };
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Csrf", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-CSRF-TOKEN",
        Description = "Get /api/auth/csrf, then paste its token here. Fetch a new token after login/logout."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Csrf", document)] = []
    });
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-MultiPlaner.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
});

builder.Services.Configure<IdentityOptions>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = PasswordLimits.MinLength;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase = true;
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException(
                           "Configure ConnectionStrings:DefaultConnection before starting the API.");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(5)));
builder.Services.AddUserSessions();
builder.Services.AddScoped<RoomService>();
builder.Services.AddScoped<EventTypeService>();
builder.Services.AddScoped<MemberManagementService>();
builder.Services.AddScoped<UserSettingsService>();
builder.Services.AddSingleton<AvatarStorage>();
builder.Services.AddScoped<AvatarService>();
builder.Services.AddScoped<RoomMutationService>();
builder.Services.AddScoped<RoomMembershipService>();
builder.Services.AddScoped<EventService>();
builder.Services.AddScoped<RoomAccessService>();
builder.Services.AddHostedService<RoomArchivalWorker>();
builder.Services.AddScoped<InvitationService>();
builder.Services.AddSingleton<InvitationLinkBuilder>();
builder.Services.AddScoped<UserFolderService>();
builder.Services.AddSingleton<RoomCoverStorage>();
builder.Services.AddScoped<RoomCoverService>();
builder.Services.AddScoped<PollService>();
builder.Services.AddCors(options => options.AddPolicy("BrowserClient", policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyMethod()
    .AllowAnyHeader()
    .AllowCredentials()));

WebApplication app;
try
{
    app = builder.Build();
}
catch
{
    DisposeAll(dpCertificates);
    throw;
}

app.Lifetime.ApplicationStopped.Register(() => DisposeAll(dpCertificates));

if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await DatabaseInitializer.MigrateAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
}

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("BrowserClient");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.Run();

static X509Certificate2 LoadPrivateKeyCert(string path, string? password, List<X509Certificate2> tracked)
{
    var cert = X509CertificateLoader.LoadPkcs12FromFile(path, password);
    if (!cert.HasPrivateKey)
    {
        cert.Dispose();
        DisposeAll(tracked);
        throw new InvalidOperationException($"DataProtection certificate '{path}' must contain a private key.");
    }

    tracked.Add(cert);
    return cert;
}

static void DisposeAll(List<X509Certificate2> certs)
{
    foreach (var cert in certs)
    {
        cert.Dispose();
    }
}

public partial class Program;