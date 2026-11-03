using System.Globalization;
using MultiPlanerSharedModels.Services;

namespace MultiPlanerAPI.UnitTests;

[CollectionDefinition("Application culture", DisableParallelization = true)]
public sealed class AppCultureCollection;

[Collection("Application culture")]
public sealed class AppCultureTests
{
    [Fact]
    public void EnglishUkOverridesPolishDeviceCultureWithoutChangingTimeZone()
    {
        var current = CultureInfo.CurrentCulture;
        var currentUi = CultureInfo.CurrentUICulture;
        var defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var defaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        var zone = TimeZoneInfo.Local.Id;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pl-PL");
            AppCulture.Apply();
            Assert.Equal("en-GB", CultureInfo.CurrentCulture.Name);
            Assert.Equal("en-GB", CultureInfo.CurrentUICulture.Name);
            Assert.Equal("en-GB", CultureInfo.DefaultThreadCurrentCulture!.Name);
            Assert.Equal("en-GB", CultureInfo.DefaultThreadCurrentUICulture!.Name);
            var value = new DateTime(2026, 11, 1, 17, 5, 0);
            Assert.Equal("November", value.ToString("MMMM"));
            Assert.Equal("01/11/2026", value.ToString("dd/MM/yyyy"));
            Assert.Equal("17:05", value.ToString("HH:mm"));
            Assert.Equal(zone, TimeZoneInfo.Local.Id);
        }
        finally
        {
            CultureInfo.CurrentCulture = current;
            CultureInfo.CurrentUICulture = currentUi;
            CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = defaultUi;
        }
    }
}