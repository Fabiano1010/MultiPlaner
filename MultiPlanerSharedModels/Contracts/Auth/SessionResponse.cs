namespace MultiPlanerSharedModels.Contracts.Auth;

// Only one of UserId and GuestSessionId is populated. No Identity entity is exposed.
public sealed record SessionResponse(
    string Kind,
    int? UserId,
    Guid? GuestSessionId,
    string DisplayName,
    string? Email,
    string? CountryCode,
    string? TimeZoneId,
    string? AvatarUrl,
    string? ContactDetails,
    DateTimeOffset? ExpiresAtUtc);

public sealed record CsrfResponse(string Token, string HeaderName);
