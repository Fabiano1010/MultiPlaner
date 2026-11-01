namespace MultiPlanerAPI.Modules.Users;
public sealed class AvatarStorage(IConfiguration config, IWebHostEnvironment environment, ILogger<AvatarStorage> logger)
{
    private readonly string root = Path.GetFullPath(config["Storage:AvatarRoot"] ?? Path.Combine(environment.ContentRootPath, "data", "avatars"));
    public async Task<string> SaveAsync(Stream source, CancellationToken ct) {
        Directory.CreateDirectory(root);
        var key = Guid.NewGuid().ToString("N");
        try {
            await using var output = new FileStream(PathFor(key), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await source.CopyToAsync(output, ct);
            return key;
        } catch { Delete(key); throw; }
    }
    public Stream Open(string key) => new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read);
    public void Delete(string? key) {
        if (key == null) return;
        try { File.Delete(PathFor(key)); }
        catch (IOException ex) { logger.LogWarning(ex, "Could not clean up avatar {Key}", key); }
        catch (UnauthorizedAccessException ex) { logger.LogWarning(ex, "Could not clean up avatar {Key}", key); }
    }
    private string PathFor(string key) => key.Length == 32 && key.All(char.IsAsciiHexDigit)
        ? Path.Combine(root, key) : throw new InvalidOperationException("Invalid avatar storage key.");
}
