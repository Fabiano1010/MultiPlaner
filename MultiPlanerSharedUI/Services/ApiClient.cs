using System.Net.Http.Json;
using MultiPlanerSharedModels.Contracts.Rooms;

namespace MultiPlanerSharedUI.Services;

public class ApiClient(HttpClient http)
{
    public async Task<string> ImageDataAsync(string url)
    {
        using var response = await http.GetAsync(url);
        await EnsureSuccessAsync(response);
        var type = response.Content.Headers.ContentType?.MediaType;
        if (type is not ("image/png" or "image/jpeg" or "image/webp"))
            throw new InvalidOperationException("Unsupported image format.");
        return $"data:{type};base64,{Convert.ToBase64String(await response.Content.ReadAsByteArrayAsync())}";
    }

    public async Task UploadAsync(string url, Microsoft.AspNetCore.Components.Forms.IBrowserFile file,
        long maximumBytes)
    {
        await using var stream = file.OpenReadStream(maximumBytes);
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(stream), "file", file.Name);
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = form };
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync());
        using var response = await http.SendAsync(request);
        await EnsureSuccessAsync(response);
    }

    private async Task<string> GetCsrfAsync()
    {
        var r = await http.GetFromJsonAsync<CsrfResponse>("api/auth/csrf");
        return r!.Token;
    }

    public async Task<T?> GetAsync<T>(string url, CancellationToken ct = default)
    {
        var res = await http.GetAsync(url, ct);
        await EnsureSuccessAsync(res);
        return await res.Content.ReadFromJsonAsync<T>(ct);
    }

    public Task<RoomResponse?> GetRoomAsync(int id, CancellationToken ct = default)
    {
        return GetAsync<RoomResponse>($"api/rooms/{id}", ct);
    }

    public Task<HttpResponseMessage> PostAsync(string url, object? body = null)
    {
        return SendAsync(HttpMethod.Post, url, body);
    }

    public Task<HttpResponseMessage> PatchAsync(string url, object? body = null)
    {
        return SendAsync(HttpMethod.Patch, url, body);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
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
                else if (!string.IsNullOrWhiteSpace(problem.Detail))
                    message = problem.Detail;
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