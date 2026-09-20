using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MultiPlanerWeb;
using MultiPlanerSharedModels.Services;
using MultiPlanerSharedModels.Models;
using MultiPlanerSharedUI.Services;
using Microsoft.AspNetCore.Components.Authorization;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<BrowserCredentialsHandler>();
builder.Services.AddHttpClient("api", c =>
        c.BaseAddress = new Uri("https://localhost:7157/"))
    .AddHttpMessageHandler<BrowserCredentialsHandler>();

builder.Services.AddScoped(sp => 
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("api"));

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<ApiAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => 
    sp.GetRequiredService<ApiAuthStateProvider>());
builder.Services.AddScoped<AuthService>();

builder.Services.AddScoped<EventService>();

await builder.Build().RunAsync();