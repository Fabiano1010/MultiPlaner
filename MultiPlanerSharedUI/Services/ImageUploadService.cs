using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;

namespace MultiPlanerSharedUI.Services;

public sealed class ImageUploadService(ApiClient api, IJSRuntime js)
{
    public async Task UploadAsWebpAsync(
        string url, IBrowserFile file, long maxSourceBytes,
        int maxWidth, int maxHeight, bool crop = false, double quality = 0.8)
    {
        using var source = new MemoryStream();
        await using (var input = file.OpenReadStream(maxSourceBytes))
            await input.CopyToAsync(source);

        await using var module = await js.InvokeAsync<IJSObjectReference>(
            "import", "./_content/MultiPlanerSharedUI/js/image.js");
        var webp = await module.InvokeAsync<byte[]>(
            "toWebp", source.ToArray(), maxWidth, maxHeight, quality, crop);

        using var upload = new MemoryStream(webp);
        await api.UploadAsync(url, upload, Path.ChangeExtension(file.Name, ".webp"), "image/webp");
    }
}