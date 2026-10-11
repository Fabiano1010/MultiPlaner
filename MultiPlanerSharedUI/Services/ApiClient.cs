using System.Net.Http.Json;
using System.Net.Http.Json;
using MultiPlanerSharedModels.Contracts.Auth;

namespace MultiPlanerSharedUI.Services;

public class ApiClient(HttpClient http) {
    private async Task<string> GetCsrfAsync() {
        var r = await http.GetFromJsonAsync<CsrfResponse>("api/auth/csrf");
        return r!.Token;
    }
    public Task<HttpResponseMessage> PostAsync(string url, object? body = null)
        => SendAsync(HttpMethod.Post, url, body);

    public Task<HttpResponseMessage> PatchAsync(string url, object? body = null)
        => SendAsync(HttpMethod.Patch, url, body);
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync());
        return await http.SendAsync(req);
    }
    
    public static async Task EnsureSuccessAsync(HttpResponseMessage res) {
        if (res.IsSuccessStatusCode) return;

        var message = res.ReasonPhrase ?? "Unknown error";
        string? code = null;
        try {
            var problem = await res.Content.ReadFromJsonAsync<ApiProblem>();
            if (problem is not null) {
                code = problem.Code;
                if (problem.Errors is { Count: > 0 })
                    message = string.Join("; ", problem.Errors.SelectMany(e => e.Value));
                else if (!string.IsNullOrWhiteSpace(problem.Title))
                    message = problem.Title;
            }
        }
        catch {
            // response is not Json
        }

        throw new ApiException(message, (int)res.StatusCode, code);
    }

    
    
}