using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace FlutterLogs.Api.Infrastructure.Auth;

public interface IPasswordHasher
{
    Task<string> HashAsync(string password, CancellationToken cancellationToken = default);

    Task<bool> VerifyAsync(string password, string encodedHash, CancellationToken cancellationToken = default);
}

/// <summary>
/// Argon2id with the parameters from <c>docs/security.md</c>. The encoded value is a
/// PHC string, so parameters travel with the hash and can be raised later without
/// invalidating existing passwords.
/// </summary>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    private const string Algorithm = "argon2id";
    private const int Version = 19;
    private const int MemorySizeKb = 65_536;
    private const int Iterations = 3;
    private const int DegreeOfParallelism = 1;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public async Task<string> HashAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);
        cancellationToken.ThrowIfCancellationRequested();

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = await ComputeAsync(
            password,
            salt,
            MemorySizeKb,
            Iterations,
            DegreeOfParallelism,
            cancellationToken).ConfigureAwait(false);

        return string.Join(
            '$',
            string.Empty,
            Algorithm,
            $"v={Version}",
            $"m={MemorySizeKb},t={Iterations},p={DegreeOfParallelism}",
            Base64UrlEncode(salt),
            Base64UrlEncode(hash));
    }

    public async Task<bool> VerifyAsync(
        string password,
        string encodedHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (!TryParse(encodedHash, out var parsed))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var computed = await ComputeAsync(
            password,
            parsed.Salt,
            parsed.MemorySizeKb,
            parsed.Iterations,
            parsed.Parallelism,
            cancellationToken).ConfigureAwait(false);

        return CryptographicOperations.FixedTimeEquals(computed, parsed.Hash);
    }

    private static async Task<byte[]> ComputeAsync(
        string password,
        byte[] salt,
        int memorySizeKb,
        int iterations,
        int parallelism,
        CancellationToken cancellationToken)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memorySizeKb,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };

        cancellationToken.ThrowIfCancellationRequested();

        return await argon2.GetBytesAsync(HashBytes).ConfigureAwait(false);
    }

    private static bool TryParse(string? encodedHash, out ParsedHash parsed)
    {
        parsed = default;

        if (string.IsNullOrWhiteSpace(encodedHash))
        {
            return false;
        }

        var parts = encodedHash.Split('$');

        if (parts.Length != 6 || parts[0].Length != 0 || parts[1] != Algorithm)
        {
            return false;
        }

        if (!parts[2].StartsWith("v=", StringComparison.Ordinal)
            || !int.TryParse(parts[2][2..], out var version)
            || version != Version)
        {
            return false;
        }

        var parameters = parts[3].Split(',');
        var values = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var parameter in parameters)
        {
            var pair = parameter.Split('=', 2);
            if (pair.Length != 2 || !int.TryParse(pair[1], out var value))
            {
                return false;
            }

            values[pair[0]] = value;
        }

        if (!values.TryGetValue("m", out var memory)
            || !values.TryGetValue("t", out var iterations)
            || !values.TryGetValue("p", out var parallelism))
        {
            return false;
        }

        // Refuse absurd parameters before allocating memory.
        if (memory is < 8_192 or > 262_144
            || iterations is < 1 or > 10
            || parallelism is < 1 or > 8)
        {
            return false;
        }

        try
        {
            var salt = Base64UrlDecode(parts[4]);
            var hash = Base64UrlDecode(parts[5]);

            if (salt.Length < 8 || hash.Length != HashBytes)
            {
                return false;
            }

            parsed = new ParsedHash(salt, hash, memory, iterations, parallelism);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        var padding = (4 - (normalized.Length % 4)) % 4;
        return Convert.FromBase64String(normalized + new string('=', padding));
    }

    private readonly record struct ParsedHash(
        byte[] Salt,
        byte[] Hash,
        int MemorySizeKb,
        int Iterations,
        int Parallelism);
}
