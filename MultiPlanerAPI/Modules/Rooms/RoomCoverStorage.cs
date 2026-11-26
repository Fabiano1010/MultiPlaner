using MultiPlanerAPI.Infrastructure;

namespace MultiPlanerAPI.Modules.Rooms;

public sealed class RoomCoverStorage(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<RoomCoverStorage> logger)
    : LocalFileStorage(
        configuration["Storage:CoverRoot"] ?? Path.Combine(environment.ContentRootPath, "data", "room-covers"),
        "cover",
        logger);
