namespace MultiPlanerAPI.Modules.Rooms;

public interface IRoomCoverStorage
{
    Task<string> SaveAsync(Stream stream, CancellationToken cancellationToken);
    Stream OpenRead(string key);
    Task DeleteAsync(string key);
}

public sealed class LocalRoomCoverStorage(IConfiguration configuration, IWebHostEnvironment environment)
    : IRoomCoverStorage
{
    private readonly string root = Path.GetFullPath(configuration["Storage:CoverRoot"] ??
        Path.Combine(environment.ContentRootPath, "data", "room-covers"));

    public async Task<string> SaveAsync(Stream stream, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        var key = Guid.NewGuid().ToString("N");
        var path = Path.Combine(root, key);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 81920, FileOptions.Asynchronous);
        try { await stream.CopyToAsync(output, cancellationToken); }
        catch { output.Close(); File.Delete(path); throw; }
        return key;
    }

    public Stream OpenRead(string key) => new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read);

    public Task DeleteAsync(string key)
    {
        File.Delete(PathFor(key));
        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        if (key.Length != 32 || key.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidOperationException("Invalid cover storage key.");
        return Path.Combine(root, key);
    }
}
