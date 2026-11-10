using Microsoft.AspNetCore.Components.Authorization;

namespace MultiPlanerSharedUI.Services;

public class ApiException(string message, int status, string? code = null) : Exception(message)
{
    public int Status { get; } = status;
    public string? Code { get; } = code;
}

public record ApiProblem(
    string? Title,
    int? Status,
    string? Code,
    Dictionary<string, string[]>? Errors,
    string? Detail);

public record CsrfResponse(string Token);

public class AuthService(ApiAuthStateProvider state, ApiClient client)
{
    public async Task<AuthenticationState> LoginAsync(string email, string password, bool rememberMe) {
        using var res = await client.PostAsync("api/auth/login", new { email, password, rememberMe });
        await ApiClient.EnsureSuccessAsync(res);
        var refreshed = await state.RefreshAuthStateAsync();
        if (!refreshed.User.HasClaim("kind", "user"))
            throw new ApiException("Could not verify your account session. Try again.", 503);

        return refreshed;
    }

    public async Task RegisterAsync(string name, string email, string password,
        string countryCode, string timeZoneId) {
        var res = await client.PostAsync("api/auth/register",
            new { displayName = name, email, password, countryCode, timeZoneId });
        await ApiClient.EnsureSuccessAsync(res);
    }

    public async Task LogoutAsync() {
        var res = await client.PostAsync("api/auth/logout");
        await ApiClient.EnsureSuccessAsync(res);
        state.NotifyAuthChanged();
    }
}
