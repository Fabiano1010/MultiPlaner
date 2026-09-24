using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using MultiPlanerWeb;
using MultiPlanerSharedModels.Services;
using MultiPlanerSharedModels.Models;
using MultiPlanerSharedUI.Services;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Main Components
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// HttpClient to API (with cookies)
builder.Services.AddScoped<BrowserCredentialsHandler>();
builder.Services.AddHttpClient("api", c =>
        c.BaseAddress = new Uri("https://localhost:7157/"))
    .AddHttpMessageHandler<BrowserCredentialsHandler>();
builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("api"));

// Authorization
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<ApiAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<ApiAuthStateProvider>());
builder.Services.AddScoped<AuthService>();

// Domain Services
builder.Services.AddScoped<EventService>();

var host = builder.Build();

// Set culture based on browser settings
try
{
    var js = host.Services.GetRequiredService<IJSInProcessRuntime>();
    var browserCulture = js.Invoke<string>("eval", "navigator.language");
    if (!string.IsNullOrWhiteSpace(browserCulture))
    {
        var culture = new CultureInfo(browserCulture);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
catch
{
    // default culture will be used
}

await host.RunAsync();