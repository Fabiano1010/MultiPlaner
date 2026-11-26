namespace MultiPlanerAPI.Infrastructure;

internal static class PublicWebUrl
{
    public static string GetRequiredBase(IConfiguration configuration, string configKey, string errorMessage)
    {
        var value = configuration[configKey];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 ||
            uri.Fragment.Length != 0)
        {
            throw new InvalidOperationException(errorMessage);
        }

        return $"{uri.GetLeftPart(UriPartial.Authority)}{uri.AbsolutePath.TrimEnd('/')}";
    }
}
