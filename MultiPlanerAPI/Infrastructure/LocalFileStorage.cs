namespace MultiPlanerAPI.Infrastructure;

public abstract class LocalFileStorage(string root, string label, ILogger logger)
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private readonly string root = Path.GetFullPath(root);

    public async Task<string> SaveAsync(Stream source, CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        var key = Guid.NewGuid().ToString("N");
        try
        {
            await using var output = new FileStream(PathFor(key), FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous);
            await source.CopyToAsync(output, ct);
            return key;
        }
        catch
        {
            Delete(key);
            throw;
        }
    }

    public Stream Open(string key)
    {
        return new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public void Delete(string? key)
    {
        if (key is null)
        {
            return;
        }

        try
        {
            File.Delete(PathFor(key));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not clean up {Label} {Key}", label, key);
        }
    }

    public async Task SaveValidatedImageAsync(
        IFormFile file,
        long maxBytes,
        string sizeError,
        string shortHeaderError,
        Func<string, string, CancellationToken, Task<string?>> commitAsync,
        CancellationToken ct)
    {
        if (file.Length <= 0 || file.Length > maxBytes)
        {
            throw ApiException.Validation("file", sizeError);
        }

        await using var source = file.OpenReadStream();
        var header = new byte[12];
        if (await source.ReadAtLeastAsync(header, 12, false, ct) < 12)
        {
            throw ApiException.Validation("file", shortHeaderError);
        }

        var contentType = DetectImageType(header)
                          ?? throw ApiException.Validation("file", "Use JPEG, PNG or WebP.");

        await using var upload = file.OpenReadStream();
        var key = await SaveAsync(upload, ct);
        string? oldKey;
        try
        {
            oldKey = await commitAsync(key, contentType, ct);
        }
        catch
        {
            Delete(key);
            throw;
        }

        Delete(oldKey);
    }

    public (Stream Stream, string ContentType) OpenOrThrow(
        string? key, string? contentType, string notFoundCode, string notFoundMessage)
    {
        if (key is null)
        {
            throw new ApiException(StatusCodes.Status404NotFound, notFoundCode, notFoundMessage);
        }

        try
        {
            return (Open(key), contentType!);
        }
        catch (FileNotFoundException)
        {
            throw new ApiException(StatusCodes.Status404NotFound, notFoundCode, notFoundMessage);
        }
    }

    internal static string? DetectImageType(ReadOnlySpan<byte> h)
    {
        if (h.Length < 12)
        {
            return null;
        }

        if (h[0] == 255 && h[1] == 216 && h[2] == 255)
        {
            return "image/jpeg";
        }

        if (h[..8].SequenceEqual(PngSignature))
        {
            return "image/png";
        }

        if (h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    private string PathFor(string key)
    {
        return key.Length == 32 && key.All(char.IsAsciiHexDigit)
            ? Path.Combine(root, key)
            : throw new InvalidOperationException($"Invalid {label} storage key.");
    }
}
