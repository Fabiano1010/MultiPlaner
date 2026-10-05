using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace MultiPlanerAPI.Modules.Rooms;

internal static class InvitationTokens
{
    public static string Generate() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

    public static bool IsWellFormed(string? token) =>
        token is { Length: 43 } &&
        token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
