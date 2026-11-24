namespace MultiPlanerAPI.Modules.Users;

internal sealed record CachedGuestExpiry(DateTimeOffset? ExpiresAtUtc);
