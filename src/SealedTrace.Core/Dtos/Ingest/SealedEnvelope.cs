namespace Arovyx.SealedTrace.Core.Dtos.Ingest;

public sealed class SealedEnvelope
{
    public int V { get; set; }
    public Guid BatchId { get; set; }
    public Guid ProjectId { get; set; }
    public DateTime CreatedAt { get; set; }
    public int EventCount { get; set; }
    public string Alg { get; set; } = string.Empty;
    public string WrappedKey { get; set; } = string.Empty;
    public string Iv { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Ciphertext { get; set; } = string.Empty;
}
