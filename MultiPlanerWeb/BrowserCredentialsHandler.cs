using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace MultiPlanerWeb;

public class BrowserCredentialsHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, ct);
    }
}