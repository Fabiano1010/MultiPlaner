using System.Net.Http.Json;
using MultiPlanerSharedModels.Contracts.Auth;

namespace MultiPlanerSharedUI.Services;

public class ApiException(string message, int status, string? code = null) : Exception(message)
{
    public int Status { get; } = status;
    public string? Code { get; } = code;
}

public record ApiProblem(string? Title, int? Status, string? Code, Dictionary<string, string[]>? Errors);

public record CsrfResponse(string Token);

public class AuthService(HttpClient http, ApiAuthStateProvider state)
{
    private async Task<string> GetCsrfAsync()
    {
        var r = await http.GetFromJsonAsync<CsrfResponse>("api/auth/csrf");
        return r!.Token;
    }

    private async Task<HttpResponseMessage> PostAsync(string url, object? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync());
        return await http.SendAsync(req);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode) return;

        var message = res.ReasonPhrase ?? "Unknown error";
        string? code = null;
        try
        {
            var problem = await res.Content.ReadFromJsonAsync<ApiProblem>();
            if (problem is not null)
            {
                code = problem.Code;
                if (problem.Errors is { Count: > 0 })
                    message = string.Join("; ", problem.Errors.SelectMany(e => e.Value));
                else if (!string.IsNullOrWhiteSpace(problem.Title))
                    message = problem.Title;
            }
        }
        catch
        {
            // response is not Json
        }

        throw new ApiException(message, (int)res.StatusCode, code);
    }

    public async Task LoginAsync(string email, string password, bool rememberMe)
    {
        var res = await PostAsync("api/auth/login", new { email, password });
        await EnsureSuccessAsync(res);
        state.NotifyAuthChanged();
    }

    public async Task RegisterAsync(string name, string email, string password,
        string countryCode, string timeZoneId)
    {
        var res = await PostAsync("api/auth/register",
            new { displayName = name, email, password, countryCode, timeZoneId });
        await EnsureSuccessAsync(res);
    }

    public async Task LogoutAsync()
    {
        var res = await PostAsync("api/auth/logout");
        await EnsureSuccessAsync(res);
        state.NotifyAuthChanged();
    }
}