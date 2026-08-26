using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MultiPlanerWeb;
using MultiPlanerSharedModels.Services;
using MultiPlanerSharedModels.Models;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri("http://localhost:5147/") 
});

builder.Services.AddScoped<EventService>();

await builder.Build().RunAsync();