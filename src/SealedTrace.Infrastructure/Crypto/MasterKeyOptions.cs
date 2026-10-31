namespace Arovyx.SealedTrace.Infrastructure.Crypto;

public sealed class MasterKeyOptions
{
    public const string SectionName = "SealedTrace";

    public string? MasterKey { get; set; }
    public int MasterKeyVersion { get; set; } = 1;
}
