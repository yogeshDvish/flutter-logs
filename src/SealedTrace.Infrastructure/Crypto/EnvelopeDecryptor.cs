using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arovyx.SealedTrace.Core.Dtos.Ingest;
using Arovyx.SealedTrace.Core.Interfaces;

namespace Arovyx.SealedTrace.Infrastructure.Crypto;

/// <summary>
/// Decrypts sealed envelopes: RSA-OAEP-SHA256 unwrap + AES-256-GCM decrypt + UTF-8 JSON parse.
/// </summary>
public sealed class EnvelopeDecryptor : IEnvelopeDecryptor
{
    private const string ExpectedAlgorithm = "RSA-OAEP-2048-SHA256+AES-256-GCM";
    private const int AesKeySize = 32;
    private const int GcmNonceSize = 12;
    private const int GcmTagSize = 16;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public Task<List<LogEvent>> DecryptAsync(
        SealedEnvelope envelope,
        string privateKeyPem,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Alg != ExpectedAlgorithm)
        {
            throw new InvalidAlgorithmException(envelope.Alg);
        }

        var wrappedKey = Convert.FromBase64String(envelope.WrappedKey);
        var iv = Convert.FromBase64String(envelope.Iv);
        var tag = Convert.FromBase64String(envelope.Tag);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);

        if (iv.Length != GcmNonceSize)
        {
            throw new InvalidEnvelopeException("IV must be exactly 12 bytes.");
        }

        if (tag.Length != GcmTagSize)
        {
            throw new InvalidEnvelopeException("Tag must be exactly 16 bytes.");
        }

        // RSA-OAEP-SHA256 unwrap: recover the 32-byte AES key.
        byte[] aesKey;
        using (var rsa = RSA.Create())
        {
            rsa.ImportFromPem(privateKeyPem);
            aesKey = rsa.Decrypt(wrappedKey, RSAEncryptionPadding.OaepSHA256);
        }

        if (aesKey.Length != AesKeySize)
        {
            throw new InvalidEnvelopeException("Unwrapped AES key is not 32 bytes.");
        }

        // AES-256-GCM decrypt.
        byte[] plaintext;
        using (var aesGcm = new AesGcm(aesKey, GcmTagSize))
        {
            plaintext = new byte[ciphertext.Length];
            aesGcm.Decrypt(iv, ciphertext, tag, plaintext);
        }

        // Parse UTF-8 JSON array of LogEvent.
        var events = JsonSerializer.Deserialize<List<LogEvent>>(plaintext, JsonOptions)
            ?? throw new InvalidPayloadException("Decrypted payload is not a JSON array.");

        return Task.FromResult(events);
    }
}

public sealed class InvalidAlgorithmException : Exception
{
    public InvalidAlgorithmException(string alg) : base($"Unsupported algorithm: {alg}.") { }
}

public sealed class InvalidEnvelopeException : Exception
{
    public InvalidEnvelopeException(string message) : base(message) { }
}

public sealed class InvalidPayloadException : Exception
{
    public InvalidPayloadException(string message) : base(message) { }
}
