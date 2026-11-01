using MultiPlanerSharedModels.Contracts.Users;
namespace MultiPlanerAPI.UnitTests;
public sealed class ThemeResolverTests
{
    [Theory]
    [InlineData("#FFFFFF", "#000000")]
    [InlineData("#000000", "#FFFFFF")]
    [InlineData("#777777", "#000000")]
    [InlineData("#0000FF", "#FFFFFF")]
    public void SelectsContrastingText(string background, string expected) => Assert.Equal(expected, ThemeResolver.Text(background));
    [Fact] public void VeryDarkAndLightSchemesKeepIndependentSurfaceText() {
        foreach (var color in new[] { "#000000", "#FFFFFF" }) {
            var resolved = ThemeResolver.Resolve(new() { Background = color, Surface = color, Accent = color, Border = color });
            Assert.NotEqual(resolved["--app-background"], resolved["--app-text"]);
            Assert.NotEqual(resolved["--surface"], resolved["--surface-text"]);
            Assert.NotEqual(resolved["--accent"], resolved["--accent-text"]);
        }
    }
}
