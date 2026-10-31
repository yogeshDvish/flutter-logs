namespace Arovyx.SealedTrace.Core.Interfaces;

public interface IProjectKeyProtector
{
    byte[] EncryptPrivateKey(string privateKeyPem, int masterKeyVersion);

    string DecryptPrivateKey(byte[] encryptedPrivateKey, int masterKeyVersion);
}
