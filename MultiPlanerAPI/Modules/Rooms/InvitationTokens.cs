using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace MultiPlanerAPI.Modules.Rooms;

internal static class InvitationTokens
{
    public static IDataProtector CreateProtector(IDataProtectionProvider provider)
    {
        return provider.CreateProtector("MultiPlaner.RoomInvitation.Token.v1");
    }

    public static string Generate()
    {
        return WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    public static string Hash(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
    }

    public static bool IsWellFormed(string? token)
    {
        return token is { Length: 43 } &&
               token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }
}