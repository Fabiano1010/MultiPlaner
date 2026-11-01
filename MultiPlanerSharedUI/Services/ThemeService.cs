using Microsoft.JSInterop;
using System.Text.RegularExpressions;
using MultiPlanerSharedModels.Contracts.Users;
namespace MultiPlanerSharedUI.Services;
public sealed class ThemeService(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? module;
    public async Task ApplyAsync(ThemePalette palette) {
        var values = new Dictionary<string, string> {
            ["--color-primary"] = palette.Primary, ["--color-primary-dim"] = palette.PanelBackground,
            ["--color-primary-dim-light"] = palette.ControlBackground, ["--color-secondary"] = palette.Secondary,
            ["--color-background"] = palette.Background, ["--selected-color"] = palette.RoomSelection,
            ["--color-day-selected"] = palette.DaySelection, ["--color-day-selected-light"] = palette.DaySelectionBackground
        };
        if (values.Values.Any(v => !Regex.IsMatch(v ?? "", ThemePalette.HexPattern))) return;
        module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/MultiPlanerSharedUI/settings.js");
        await module.InvokeVoidAsync("applyTheme", values);
    }
    public async ValueTask DisposeAsync() { if (module != null) await module.DisposeAsync(); }
}
