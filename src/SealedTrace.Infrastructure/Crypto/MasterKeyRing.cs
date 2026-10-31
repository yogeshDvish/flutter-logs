using Microsoft.Extensions.Options;

namespace Arovyx.SealedTrace.Infrastructure.Crypto;

/// <summary>
/// Holds master key versions used to encrypt/decrypt project RSA private keys.
/// The current version encrypts newly created keys; older versions remain for decryption.
/// </summary>
public sealed class MasterKeyRing
{
    private readonly Dictionary<int, byte[]> _keys;

    public MasterKeyRing(IOptions<MasterKeyOptions> options)
    {
        var opts = options.Value;
        _keys = new Dictionary<int, byte[]>();

        if (!string.IsNullOrEmpty(opts.MasterKey))
        {
            var keyBytes = Convert.FromBase64String(opts.MasterKey);
            if (keyBytes.Length != 32)
            {
                throw new InvalidOperationException(
                    "SealedTrace:MasterKey must decode to exactly 32 bytes (256 bits).");
            }
            _keys[opts.MasterKeyVersion] = keyBytes;
        }

        CurrentVersion = opts.MasterKeyVersion;
    }

    public int CurrentVersion { get; }

    public byte[] GetKey(int version)
    {
        if (!_keys.TryGetValue(version, out var key))
        {
            throw new InvalidOperationException($"Master key version {version} is not configured.");
        }
        return key;
    }
}
