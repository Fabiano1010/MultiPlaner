using System.Net.Http.Json;
using MultiPlanerSharedModels.Contracts.Auth;

namespace MultiPlanerSharedUI.Services;

public class AuthService(HttpClient http, ApiAuthStateProvider state) {

    private async Task<string> GetCsrfAsync() {
        var r = await http.GetFromJsonAsync<CsrfResponse>("api/auth/csrf");
        return r!.Token;
    }

    private async Task<HttpResponseMessage> PostAsync(string url, object? body = null) {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync());
        return await http.SendAsync(req);
    }

    public async Task<bool> LoginAsync(string email, string password, bool rememberMe) {
        var res = await PostAsync("api/auth/login", new {email, password, rememberMe});
        if (res.IsSuccessStatusCode) state.NotifyAuthChanged();
        return res.IsSuccessStatusCode;
    }
    
    public async Task<bool> RegisterAsync(string displayName, string email, string password, string countryCode, string timeZoneId) {
        var res = await PostAsync("api/auth/register", new {displayName, email, password, countryCode, timeZoneId});
        return res.IsSuccessStatusCode;
    }

    public async Task LogoutAsync() {
        var res = await PostAsync("api/auth/logout");
        if (res.IsSuccessStatusCode) state.NotifyAuthChanged();
    }
}
public record CsrfResponse(string Token);
