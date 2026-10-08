using System.Net.Http.Json;
using System.Net.Http.Json;
using MultiPlanerSharedModels.Contracts.Auth;

namespace MultiPlanerSharedUI.Services;

public class ApiClient(HttpClient http) {
    private async Task<string> GetCsrfAsync()
    {
        var r = await http.GetFromJsonAsync<CsrfResponse>("api/auth/csrf");
        return r!.Token;
    }
    public async Task<HttpResponseMessage> PostAsync(string url, object? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        req.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync());
        return await http.SendAsync(req);
    }
    
    public static async Task EnsureSuccessAsync(HttpResponseMessage res)
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

    
    
}