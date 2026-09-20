using System;
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Hosting;
using MultiPlanerSharedModels.Services;
using MultiPlanerSharedUI.Services;

namespace MultiPlanerApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder.UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

        // HTTPS jest wymagane, bo cookies API mają flagę Secure
        string apiBaseUrl = DeviceInfo.Platform == DevicePlatform.Android
            ? "https://10.0.2.2:7157/"
            : "https://localhost:7157/";

        // Cookies trzymane w pamięci (jeden kontener na całą aplikację)
        var cookies = new CookieContainer();
        builder.Services.AddSingleton(cookies);

        builder.Services.AddSingleton(sp =>
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = cookies,
                UseCookies = true
            };

#if DEBUG
            // Tylko development: akceptuj certyfikat deweloperski
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
#endif

            return new HttpClient(handler)
            {
                BaseAddress = new Uri(apiBaseUrl)
            };
        });

        // Autoryzacja (w MAUI Blazor Hybrid scope jest jeden, więc Singleton)
        builder.Services.AddAuthorizationCore();
        builder.Services.AddSingleton<ApiAuthStateProvider>();
        builder.Services.AddSingleton<AuthenticationStateProvider>(sp =>
            sp.GetRequiredService<ApiAuthStateProvider>());
        builder.Services.AddSingleton<AuthService>();

        builder.Services.AddSingleton<EventService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}