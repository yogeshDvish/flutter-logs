using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Arovyx.SealedTrace.Core.Dtos.Ingest;
using Arovyx.SealedTrace.Infrastructure.Crypto;

namespace Arovyx.SealedTrace.Tests.Unit;

public sealed class EnvelopeDecryptorTests
{
    private const string ExpectedAlgorithm = "RSA-OAEP-2048-SHA256+AES-256-GCM";

    [Fact]
    public async Task Decrypt_valid_envelope_returns_log_events()
    {
        // 1. Generate RSA key pair.
        using var rsa = RSA.Create(2048);
        var privateKeyPem = rsa.ExportRSAPrivateKeyPem();
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();

        // 2. Create test log events.
        var logEvents = new List<Dictionary<string, object>>
        {
            new()
            {
                ["id"] = Guid.NewGuid().ToString(),
                ["seq"] = 1,
                ["ts"] = DateTime.UtcNow.ToString("o"),
                ["level"] = "info",
                ["tag"] = "auth",
                ["message"] = "User logged in",
            },
            new()
            {
                ["id"] = Guid.NewGuid().ToString(),
                ["seq"] = 2,
                ["ts"] = DateTime.UtcNow.AddSeconds(1).ToString("o"),
                ["level"] = "error",
                ["tag"] = "db",
                ["message"] = "Connection failed",
            },
        };

        var eventsJson = JsonSerializer.Serialize(logEvents);
        var eventsBytes = Encoding.UTF8.GetBytes(eventsJson);

        // 3. Generate AES key and encrypt events with AES-256-GCM.
        var aesKey = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[eventsBytes.Length];
        var tag = new byte[16];

        using (var aesGcm = new AesGcm(aesKey, 16))
        {
            aesGcm.Encrypt(nonce, eventsBytes, ciphertext, tag);
        }

        // 4. Wrap AES key with RSA-OAEP-SHA256.
        var wrappedKey = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);

        // 5. Build sealed envelope.
        var envelope = new SealedEnvelope
        {
            V = 1,
            BatchId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            EventCount = logEvents.Count,
            Alg = ExpectedAlgorithm,
            WrappedKey = Convert.ToBase64String(wrappedKey),
            Iv = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Ciphertext = Convert.ToBase64String(ciphertext),
        };

        // 6. Decrypt.
        var decryptor = new EnvelopeDecryptor();
        var decrypted = await decryptor.DecryptAsync(envelope, privateKeyPem);

        decrypted.Should().HaveCount(2);
        decrypted[0].Level.Should().Be("info");
        decrypted[0].Tag.Should().Be("auth");
        decrypted[0].Message.Should().Be("User logged in");
        decrypted[1].Level.Should().Be("error");
        decrypted[1].Tag.Should().Be("db");
        decrypted[1].Message.Should().Be("Connection failed");
    }

    [Fact]
    public async Task Decrypt_wrong_algorithm_throws()
    {
        using var rsa = RSA.Create(2048);
        var privateKeyPem = rsa.ExportRSAPrivateKeyPem();

        var envelope = new SealedEnvelope
        {
            V = 1,
            BatchId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Alg = "WRONG-ALGORITHM",
            WrappedKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            Iv = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)),
            Tag = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
            Ciphertext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
        };

        var decryptor = new EnvelopeDecryptor();

        Func<Task<List<LogEvent>>> act = async () => await decryptor.DecryptAsync(envelope, privateKeyPem);

        await act.Should().ThrowAsync<Arovyx.SealedTrace.Infrastructure.Crypto.InvalidAlgorithmException>();
    }

    [Fact]
    public async Task Decrypt_tampered_ciphertext_throws()
    {
        using var rsa = RSA.Create(2048);
        var privateKeyPem = rsa.ExportRSAPrivateKeyPem();
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();

        var eventsJson = JsonSerializer.Serialize(new[] { new { id = Guid.NewGuid(), seq = 1, ts = DateTime.UtcNow.ToString("o"), level = "info", tag = "test", message = "hello" } });
        var eventsBytes = Encoding.UTF8.GetBytes(eventsJson);

        var aesKey = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[eventsBytes.Length];
        var tag = new byte[16];

        using (var aesGcm = new AesGcm(aesKey, 16))
        {
            aesGcm.Encrypt(nonce, eventsBytes, ciphertext, tag);
        }

        var wrappedKey = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);

        var envelope = new SealedEnvelope
        {
            V = 1,
            BatchId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            EventCount = 1,
            Alg = ExpectedAlgorithm,
            WrappedKey = Convert.ToBase64String(wrappedKey),
            Iv = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Ciphertext = Convert.ToBase64String(ciphertext),
        };

        // Tamper with the ciphertext.
        var tamperedBytes = Convert.FromBase64String(envelope.Ciphertext);
        tamperedBytes[tamperedBytes.Length - 1] ^= 0xFF;
        envelope.Ciphertext = Convert.ToBase64String(tamperedBytes);

        var decryptor = new EnvelopeDecryptor();

        Func<Task<List<LogEvent>>> act = async () => await decryptor.DecryptAsync(envelope, privateKeyPem);

        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task Decrypt_wrong_iv_length_throws()
    {
        using var rsa = RSA.Create(2048);
        var privateKeyPem = rsa.ExportRSAPrivateKeyPem();

        var envelope = new SealedEnvelope
        {
            V = 1,
            BatchId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Alg = ExpectedAlgorithm,
            WrappedKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            Iv = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)), // wrong: 16 bytes instead of 12
            Tag = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
            Ciphertext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
        };

        var decryptor = new EnvelopeDecryptor();

        Func<Task<List<LogEvent>>> act = async () => await decryptor.DecryptAsync(envelope, privateKeyPem);

        await act.Should().ThrowAsync<Arovyx.SealedTrace.Infrastructure.Crypto.InvalidEnvelopeException>();
    }
}
