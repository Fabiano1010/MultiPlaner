using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MultiPlanerSharedModels.Services;
using MultiPlanerSharedUI.Services;
using MultiPlanerWeb;

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
builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<AuthService>();
        builder.Services.AddScoped<ThemeService>();

// Domain Services
builder.Services.AddScoped<ImageUploadService>();
builder.Services.AddScoped<RoomSelection>();
var host = builder.Build();

AppCulture.Apply();

await host.RunAsync();