using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace FlutterLogs.Api.Infrastructure.Auth;

/// <summary>
/// Creates and verifies project ingestion keys:
/// <c>flk_&lt;env&gt;_&lt;8-char prefix&gt;_&lt;43-char base64url secret&gt;</c>.
/// The secret is never stored; only an HMAC-SHA256 digest keyed with the server
/// pepper is persisted.
/// </summary>
public static class IngestionKeyFactory
{
    public const int SecretByteLength = 32;
    public const int PrefixLength = 8;
    private const string PrefixAlphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-";

    public static IssuedIngestionKey Create(IngestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var prefix = CreatePrefix();
        var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(SecretByteLength));
        var plaintext = $"flk_{options.KeyEnvironment}_{prefix}_{secret}";

        return new IssuedIngestionKey(plaintext, prefix, ComputeHash(secret, options.Pepper));
    }

    public static byte[] ComputeHash(string secret, string pepper)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        ArgumentException.ThrowIfNullOrEmpty(pepper);

        return HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(pepper),
            Encoding.UTF8.GetBytes(secret));
    }

    public static bool FixedTimeEquals(byte[] left, byte[] right) =>
        CryptographicOperations.FixedTimeEquals(left, right);

    /// <summary>
    /// Parses a presented key into its prefix and secret. Returns false for
    /// anything that does not match the documented shape.
    /// </summary>
    public static bool TryParse(string? presented, out string prefix, out string secret)
    {
        prefix = string.Empty;
        secret = string.Empty;

        if (string.IsNullOrWhiteSpace(presented))
        {
            return false;
        }

        var parts = presented.Split('_', 4);

        if (parts.Length != 4
            || !string.Equals(parts[0], "flk", StringComparison.Ordinal)
            || parts[1].Length == 0
            || parts[2].Length != PrefixLength
            || parts[3].Length != 43)
        {
            return false;
        }

        prefix = parts[2];
        secret = parts[3];
        return true;
    }

    private static string CreatePrefix() =>
        RandomNumberGenerator.GetString(PrefixAlphabet, PrefixLength);
}

/// <summary>Plaintext is returned to the caller once and never persisted.</summary>
public sealed record IssuedIngestionKey(string Plaintext, string Prefix, byte[] Hash);
