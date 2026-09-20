using Microsoft.AspNetCore.Identity;

namespace MultiPlanerAPI.Models;

public sealed class ApplicationUser : IdentityUser<int>, ITimestamped
{
    public string DisplayName { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "PL";
    public string TimeZoneId { get; set; } = "Europe/Warsaw";
    public string? AvatarUrl { get; set; }
    public string? ContactDetails { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
