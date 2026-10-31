using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Arovyx.SealedTrace.Infrastructure.Crypto;

namespace Arovyx.SealedTrace.Tests.Unit;

public sealed class ProjectKeyProtectorTests
{
    private static MasterKeyRing CreateRing(string? masterKey = null, int version = 1)
    {
        var options = new MasterKeyOptions
        {
            MasterKey = masterKey ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            MasterKeyVersion = version,
        };
        return new MasterKeyRing(Options.Create(options));
    }

    [Fact]
    public void Encrypt_decrypt_round_trip_succeeds()
    {
        var ring = CreateRing();
        var protector = new ProjectKeyProtector(ring);

        var privateKeyPem = "-----BEGIN PRIVATE KEY-----\ntest-key-material\n-----END PRIVATE KEY-----";

        var encrypted = protector.EncryptPrivateKey(privateKeyPem, ring.CurrentVersion);
        var decrypted = protector.DecryptPrivateKey(encrypted, ring.CurrentVersion);

        decrypted.Should().Be(privateKeyPem);
    }

    [Fact]
    public void Decrypt_with_wrong_master_key_version_fails()
    {
        var ringV1 = CreateRing(version: 1);
        var ringV2 = CreateRing(version: 2);
        var protector = new ProjectKeyProtector(ringV1);

        var privateKeyPem = "-----BEGIN PRIVATE KEY-----\ntest-key-material\n-----END PRIVATE KEY-----";

        var encrypted = protector.EncryptPrivateKey(privateKeyPem, 1);

        var act = () => protector.DecryptPrivateKey(encrypted, 2);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Decrypt_tampered_ciphertext_fails()
    {
        var ring = CreateRing();
        var protector = new ProjectKeyProtector(ring);

        var privateKeyPem = "-----BEGIN PRIVATE KEY-----\ntest-key-material\n-----END PRIVATE KEY-----";

        var encrypted = protector.EncryptPrivateKey(privateKeyPem, ring.CurrentVersion);
        encrypted[encrypted.Length - 1] ^= 0xFF; // tamper last byte

        var act = () => protector.DecryptPrivateKey(encrypted, ring.CurrentVersion);
        act.Should().Throw<CryptographicException>();
    }
}
