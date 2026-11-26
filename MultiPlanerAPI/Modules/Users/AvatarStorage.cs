using MultiPlanerAPI.Infrastructure;

namespace MultiPlanerAPI.Modules.Users;

public sealed class AvatarStorage(IConfiguration config, IWebHostEnvironment environment, ILogger<AvatarStorage> logger)
    : LocalFileStorage(
        config["Storage:AvatarRoot"] ?? Path.Combine(environment.ContentRootPath, "data", "avatars"),
        "avatar",
        logger);
