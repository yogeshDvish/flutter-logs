namespace Arovyx.SealedTrace.Core.Entities;

public sealed class ProjectKey
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public string Algorithm { get; set; } = "RSA-OAEP-2048-SHA256";

    public string PublicKeyPem { get; set; } = string.Empty;

    public byte[] PrivateKeyEncrypted { get; set; } = [];

    public int MasterKeyVersion { get; set; } = 1;

    public DateTime CreatedAtUtc { get; set; }

    public Project? Project { get; set; }
}
