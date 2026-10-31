namespace Arovyx.SealedTrace.Core.Dtos.Ingest;

public sealed class IngestRequest
{
    public List<SealedEnvelope> Envelopes { get; set; } = new();
}
