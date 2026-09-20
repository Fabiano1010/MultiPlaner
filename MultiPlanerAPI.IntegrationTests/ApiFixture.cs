using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiPlanerAPI.Data;
using MultiPlanerAPI.Models;
using MultiPlanerAPI.Modules.Users;
using MultiPlanerSharedModels.Contracts.Auth;
using Testcontainers.MsSql;

namespace MultiPlanerAPI.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "SQL Server API";
}

public sealed class ApiFixture : IAsyncLifetime
{
    private MsSqlContainer? container;
    private string? databaseName;
    public ApiFactory Factory { get; private set; } = null!;
    public const string Password = "Correct-Horse9!Battery";

    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("MULTIPLANER_TEST_SQLSERVER");
        if (string.IsNullOrWhiteSpace(connection))
        {
            container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
            await container.StartAsync();
            connection = container.GetConnectionString();
        }

        // Always use a new database, including when an external server is supplied.
        databaseName = $"MultiPlanerTests_{Guid.NewGuid():N}";
        var connectionBuilder = new SqlConnectionStringBuilder(connection) { InitialCatalog = databaseName };
        Factory = new ApiFactory(connectionBuilder.ConnectionString);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.GetDbConnection().Database != databaseName)
            throw new InvalidOperationException("Refusing to run tests against a database not created by this fixture.");
        await DatabaseInitializer.MigrateAsync(db);
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (Factory is not null)
            {
                await using var scope = Factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (db.Database.GetDbConnection().Database == databaseName &&
                    databaseName?.StartsWith("MultiPlanerTests_", StringComparison.Ordinal) == true)
                    await db.Database.EnsureDeletedAsync();
                await Factory.DisposeAsync();
            }
        }
        finally
        {
            if (container is not null) await container.DisposeAsync();
        }
    }

    public HttpClient Client(bool handleCookies = true) => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = handleCookies
    });

    public static async Task<string> CsrfAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add(response!.HeaderName, response.Token);
        return response.Token;
    }

    public async Task<(HttpClient Client, SessionResponse User, string Cookie)> RegisterAndLoginAsync()
    {
        var client = Client();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        await CsrfAsync(client);
        var registration = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email, Password = Password, DisplayName = "Test User"
        });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = login.Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith(SessionAuthentication.UserCookie + "=", StringComparison.Ordinal)).Split(';')[0];
        var user = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
        await CsrfAsync(client); // CSRF tokens are bound to the authenticated identity.
        return (client, user, cookie);
    }

    public async Task<(HttpClient Client, Guid Id, string Cookie)> GuestAsync(DateTimeOffset? cookieExpiry = null)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var session = await scope.ServiceProvider.GetRequiredService<GuestSessionService>().CreateAsync("Named Guest");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, $"guest:{session.Id}"),
            new Claim(SessionAuthentication.GuestIdClaim, session.Id.ToString())
        ], SessionAuthentication.GuestScheme));
        var properties = new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = cookieExpiry ?? session.ExpiresAtUtc,
            IsPersistent = true
        };
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(SessionAuthentication.GuestScheme);
        var cookie = $"{SessionAuthentication.GuestCookie}={options.TicketDataFormat.Protect(
            new AuthenticationTicket(principal, properties, SessionAuthentication.GuestScheme))}";
        var client = Factory.CreateDefaultClient(new TestCookieHandler(cookie));
        client.BaseAddress = new Uri("https://localhost");
        return (client, session.Id, cookie);
    }
}

internal sealed class TestCookieHandler : DelegatingHandler
{
    private readonly CookieContainer cookies = new();

    public TestCookieHandler(string initialCookie) =>
        cookies.SetCookies(new Uri("https://localhost"), initialCookie);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation("Cookie", cookies.GetCookieHeader(request.RequestUri!));
        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
            foreach (var value in values) cookies.SetCookies(request.RequestUri!, value);
        return response;
    }
}

public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Database:MigrateOnStartup"] = "false",
                ["Cors:AllowedOrigins:0"] = "https://client.example"
            }));
        // Program captures its connection string before deferred configuration callbacks.
        // Replace all EF options explicitly so tests cannot use the development database.
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(5)));
        });
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }
}
