namespace MultiPlanerAPI.Infrastructure;

public abstract class LocalFileStorage(string root, string label, ILogger logger)
{
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

    private string PathFor(string key)
    {
        return key.Length == 32 && key.All(char.IsAsciiHexDigit)
            ? Path.Combine(root, key)
            : throw new InvalidOperationException($"Invalid {label} storage key.");
    }
}
