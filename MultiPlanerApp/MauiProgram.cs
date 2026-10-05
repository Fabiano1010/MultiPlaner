using System;
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Storage;
using MultiPlanerSharedModels.Services;
using MultiPlanerSharedUI.Services;

namespace MultiPlanerApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        // Hosting MAUI / Blazor Hybrid
        builder.UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });
        builder.Services.AddMauiBlazorWebView();

        // Konfiguracja (appsettings.json)
        var configuration = LoadAppSettings();
        builder.Configuration.AddConfiguration(configuration);

        // HttpClient do API (z cookies)
        var cookies = new CookieContainer();
        builder.Services.AddSingleton(cookies);
        builder.Services.AddSingleton(sp =>
            CreateApiHttpClient(cookies, sp.GetRequiredService<IConfiguration>()));

        // Autoryzacja
        builder.Services.AddAuthorizationCore();
        builder.Services.AddSingleton<ApiAuthStateProvider>();
        builder.Services.AddSingleton<AuthenticationStateProvider>(sp =>
            sp.GetRequiredService<ApiAuthStateProvider>());
        builder.Services.AddSingleton<AuthService>();

        // Serwisy domenowe
        builder.Services.AddSingleton<EventService>();

#if DEBUG
        // Narzędzia developerskie
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static IConfiguration LoadAppSettings()
    {
        using var stream = FileSystem.OpenAppPackageFileAsync("appsettings.json")
            .GetAwaiter().GetResult();

        return new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();
    }

    private static HttpClient CreateApiHttpClient(CookieContainer cookies, IConfiguration config)
    {
        // HTTPS jest wymagane, bo cookies API mają flagę Secure
        var apiBaseUrl = DeviceInfo.Platform == DevicePlatform.Android
            ? config["Api:BaseUrlAndroid"]
            : config["Api:BaseUrlDefault"];

        if (string.IsNullOrWhiteSpace(apiBaseUrl))
            throw new InvalidOperationException("Configure Api:BaseUrlDefault / Api:BaseUrlAndroid w appsettings.json.");

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

        return new HttpClient(handler) { BaseAddress = new Uri(apiBaseUrl) };
    }
}