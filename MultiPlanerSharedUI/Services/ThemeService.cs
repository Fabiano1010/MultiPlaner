using Microsoft.JSInterop;
using MultiPlanerSharedModels.Contracts.Users;
namespace MultiPlanerSharedUI.Services;
public sealed class ThemeService(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? module;
    public async Task ApplyAsync(ThemePalette palette) {
        module ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/MultiPlanerSharedUI/js/settings.js");
        await module.InvokeVoidAsync("applyTheme", ThemeResolver.Resolve(palette));
    }
    public async ValueTask DisposeAsync() { if (module != null) await module.DisposeAsync(); }
}
