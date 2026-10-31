using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace FlutterLogs.Api.Infrastructure.Auth;

/// <summary>Opaque, high-entropy tokens used for refresh and email flows.</summary>
public static class OpaqueToken
{
    public const int ByteLength = 32;

    public static string Create() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(ByteLength));

    /// <summary>SHA-256 digest stored in the database; the plaintext never is.</summary>
    public static byte[] Hash(string token) =>
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
}
