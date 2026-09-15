using System.Globalization;

namespace MultiPlanerAPI.Modules.Users;

public static class LocaleValidation
{
    private static readonly HashSet<string> Countries = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(c => new RegionInfo(c.Name).TwoLetterISORegionName)
        .Where(code => code.Length == 2).ToHashSet(StringComparer.Ordinal);

    public static Dictionary<string, string[]> GetErrors(string? displayName, string? countryCode, string? timeZoneId)
    {
        var errors = new Dictionary<string, string[]>();
        if (displayName is not null && (displayName.Trim().Length < 2 || displayName.Length > 64))
            errors["displayName"] = ["Display name must contain 2 to 64 characters."];
        if (countryCode is not null && !Countries.Contains(countryCode))
            errors["countryCode"] = ["Use a supported ISO 3166-1 alpha-2 country code."];
        if (timeZoneId is not null && !IsIanaTimeZone(timeZoneId))
            errors["timeZoneId"] = ["Use an IANA time zone, for example Europe/Warsaw or UTC."];
        return errors;
    }

    private static bool IsIanaTimeZone(string id) =>
        id == "UTC" ||
        (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out _) &&
         TimeZoneInfo.TryFindSystemTimeZoneById(id, out _));
}
