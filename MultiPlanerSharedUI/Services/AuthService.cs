using System.Net.Http.Json;
using MultiPlanerSharedModels.Contracts.Auth;
using MultiPlanerSharedModels.Services;

namespace MultiPlanerSharedUI.Services;



public class ApiException(string message, int status, string? code = null) : Exception(message)
{
    public int Status { get; } = status;
    public string? Code { get; } = code;
}

public record ApiProblem(string? Title, int? Status, string? Code, Dictionary<string, string[]>? Errors);

public record CsrfResponse(string Token);

public class AuthService(ApiAuthStateProvider state, ApiClient client)
{

    
    public async Task LoginAsync(string email, string password, bool rememberMe)
    {
        var res = await client.PostAsync("api/auth/login", new { email, password  });
        await ApiClient.EnsureSuccessAsync(res); 
        state.NotifyAuthChanged();
    }

    public async Task RegisterAsync(string name, string email, string password,
        string countryCode, string timeZoneId)
    {
        var res = await client.PostAsync("api/auth/register",
            new { displayName = name, email, password, countryCode, timeZoneId });
        await ApiClient.EnsureSuccessAsync(res);
    }

    public async Task LogoutAsync()
    {
        var res = await client.PostAsync("api/auth/logout");
        await ApiClient.EnsureSuccessAsync(res);
        state.NotifyAuthChanged();
    }
}