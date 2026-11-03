using MultiPlanerSharedModels.Contracts.Users;

namespace MultiPlanerAPI.UnitTests;

public sealed class ThemeResolverTests
{
    [Theory]
    [InlineData("#FFFFFF", "#000000")]
    [InlineData("#000000", "#FFFFFF")]
    [InlineData("#777777", "#000000")]
    [InlineData("#0000FF", "#FFFFFF")]
    public void SelectsContrastingText(string background, string expected)
    {
        Assert.Equal(expected, ThemeResolver.Text(background));
    }

    [Fact]
    public void VeryDarkAndLightSchemesKeepIndependentSurfaceText()
    {
        foreach (var color in new[] { "#000000", "#FFFFFF" })
        {
            var resolved = ThemeResolver.Resolve(new ThemePalette
                { Background = color, Surface = color, Accent = color, Border = color });
            Assert.NotEqual(resolved["--app-background"], resolved["--app-text"]);
            Assert.NotEqual(resolved["--surface"], resolved["--surface-text"]);
            Assert.NotEqual(resolved["--accent"], resolved["--accent-text"]);
        }
    }

    [Theory]
    [InlineData("#DDDDDD", "#000000", "brightness(0)")]
    [InlineData("#111111", "#FFFFFF", "brightness(0) invert(1)")]
    [InlineData("#FFEE00", "#000000", "brightness(0)")]
    [InlineData("#0000FF", "#FFFFFF", "brightness(0) invert(1)")]
    public void CardTextAndSymbolsFollowTheirActualBackground(string background, string text, string filter)
    {
        Assert.Equal(text, ThemeResolver.Text(background));
        Assert.Equal(filter, ThemeResolver.IconFilter(background));
    }

    [Fact]
    public void NavigationIsBlackForEveryScheme()
    {
        foreach (var surface in new[] { "#FFFFFF", "#111111", "#FF00FF" })
        {
            var tokens = ThemeResolver.Resolve(new ThemePalette { Surface = surface });
            Assert.Equal("#000000", tokens["--sidebar-background"]);
            Assert.Equal("#FFFFFF", tokens["--sidebar-text"]);
        }
    }
}