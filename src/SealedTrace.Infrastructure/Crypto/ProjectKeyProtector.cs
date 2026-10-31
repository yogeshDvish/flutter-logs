using System.Security.Cryptography;
using System.Text;
using Arovyx.SealedTrace.Core.Interfaces;

namespace Arovyx.SealedTrace.Infrastructure.Crypto;

/// <summary>
/// Encrypts/decrypts project RSA private keys with AES-256-GCM.
/// Envelope format: [version:1][nonce:12][tag:16][ciphertext]
/// </summary>
public sealed class ProjectKeyProtector : IProjectKeyProtector
{
    private const byte EnvelopeVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int EnvelopeHeaderSize = 1 + NonceSize + TagSize; // 29

    private readonly MasterKeyRing _masterKeyRing;

    public ProjectKeyProtector(MasterKeyRing masterKeyRing)
    {
        _masterKeyRing = masterKeyRing;
    }

    public byte[] EncryptPrivateKey(string privateKeyPem, int masterKeyVersion)
    {
        var key = _masterKeyRing.GetKey(masterKeyVersion);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintext = Encoding.UTF8.GetBytes(privateKeyPem);

        using var aes = new AesGcm(key, TagSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var envelope = new byte[EnvelopeHeaderSize + ciphertext.Length];
        envelope[0] = EnvelopeVersion;
        Buffer.BlockCopy(nonce, 0, envelope, 1, NonceSize);
        Buffer.BlockCopy(tag, 0, envelope, 1 + NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, envelope, EnvelopeHeaderSize, ciphertext.Length);

        return envelope;
    }

    public string DecryptPrivateKey(byte[] encryptedPrivateKey, int masterKeyVersion)
    {
        if (encryptedPrivateKey.Length < EnvelopeHeaderSize)
        {
            throw new InvalidOperationException("Encrypted private key envelope is too short.");
        }

        var version = encryptedPrivateKey[0];
        if (version != EnvelopeVersion)
        {
            throw new InvalidOperationException($"Unsupported envelope version: {version}.");
        }

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertext = new byte[encryptedPrivateKey.Length - EnvelopeHeaderSize];

        Buffer.BlockCopy(encryptedPrivateKey, 1, nonce, 0, NonceSize);
        Buffer.BlockCopy(encryptedPrivateKey, 1 + NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(encryptedPrivateKey, EnvelopeHeaderSize, ciphertext, 0, ciphertext.Length);

        var key = _masterKeyRing.GetKey(masterKeyVersion);

        using var aes = new AesGcm(key, TagSize);
        var plaintext = new byte[ciphertext.Length];
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}
