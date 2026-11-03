using System.Globalization;

namespace MultiPlanerSharedModels.Contracts.Users;

public static class ThemeResolver
{
    private static (int R, int G, int B) RGB(string hex)
    {
        return (int.Parse(hex[1..3], NumberStyles.HexNumber), int.Parse(hex[3..5], NumberStyles.HexNumber),
            int.Parse(hex[5..7], NumberStyles.HexNumber));
    }

    public static string Mix(string first, string second, double amount)
    {
        var a = RGB(first);
        var b = RGB(second);
        return
            $"#{(int)Math.Round(a.R + (b.R - a.R) * amount):X2}{(int)Math.Round(a.G + (b.G - a.G) * amount):X2}{(int)Math.Round(a.B + (b.B - a.B) * amount):X2}";
    }

    public static string Text(string background)
    {
        var c = RGB(background);

        static double Linear(int value)
        {
            var s = value / 255d;
            return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4);
        }

        var luminance = .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        return luminance > .179 ? "#000000" : "#FFFFFF";
    }

    public static string IconFilter(string background)
    {
        return Text(background) == "#FFFFFF" ? "brightness(0) invert(1)" : "brightness(0)";
    }

    public static IReadOnlyDictionary<string, string> Resolve(ThemePalette p)
    {
        var surfaceText = Text(p.Surface);
        const string sidebar = "#000000";
        var field = Mix(p.Surface, p.Background, .6);
        return new Dictionary<string, string>
        {
            ["--icon-filter"] = Text(p.Background) == "#FFFFFF" ? "brightness(0) invert(1)" : "brightness(0)",
            ["--sidebar-icon-filter"] = Text(sidebar) == "#FFFFFF" ? "brightness(0) invert(1)" : "brightness(0)",
            ["--accent-icon-filter"] = Text(p.Accent) == "#FFFFFF" ? "brightness(0) invert(1)" : "brightness(0)",
            ["--app-background"] = p.Background, ["--app-text"] = Text(p.Background),
            ["--surface-icon-filter"] = surfaceText == "#FFFFFF" ? "brightness(0) invert(1)" : "brightness(0)",
            ["--surface"] = p.Surface, ["--surface-text"] = surfaceText,
            ["--sidebar-background"] = sidebar, ["--sidebar-text"] = Text(sidebar),
            ["--native-color-scheme"] = Text(field) == "#FFFFFF" ? "dark" : "light",
            ["--field-icon-filter"] = IconFilter(field),
            ["--field-background"] = field, ["--field-text"] = Text(field),
            ["--accent"] = p.Accent, ["--accent-text"] = Text(p.Accent),
            ["--border"] = p.Border, ["--hover-background"] = Mix(p.Surface, surfaceText, .12),
            ["--selection-background"] = Mix(p.Background, p.Accent, .18),
            ["--selection-text"] = Text(Mix(p.Background, p.Accent, .18)),
            ["--shadow"] = Mix(p.Border, p.Background, .5)
        };
    }
}