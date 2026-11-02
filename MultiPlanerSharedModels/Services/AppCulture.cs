using System.Globalization;

namespace MultiPlanerSharedModels.Services;

public static class AppCulture
{
    public static void Apply()
    {
        var culture = CultureInfo.GetCultureInfo("en-GB");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
