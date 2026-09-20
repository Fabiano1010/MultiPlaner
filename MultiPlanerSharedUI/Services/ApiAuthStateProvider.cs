using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace MultiPlanerSharedUI.Services;

public class ApiAuthStateProvider(HttpClient http) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Console.WriteLine("ApiAuth: GET api/me...");
            var response = await http.GetAsync("api/me", cts.Token);
            Console.WriteLine($"ApiAuth: status {(int)response.StatusCode}");
            if (!response.IsSuccessStatusCode) return Anonymous;

            var me = await response.Content.ReadFromJsonAsync<MeResponse>(cancellationToken: cts.Token);
            if (me is null) return Anonymous;

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, me.DisplayName ?? ""),
                new("kind", me.Kind)
            };
            return new AuthenticationState(
                new ClaimsPrincipal(new ClaimsIdentity(claims, "cookie")));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ApiAuth: błąd {ex.GetType().Name}: {ex.Message}");
            return Anonymous;
        }
    }

    public void NotifyAuthChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}

public record MeResponse(string Kind, string? DisplayName);