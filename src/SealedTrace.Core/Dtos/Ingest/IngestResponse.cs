namespace Arovyx.SealedTrace.Core.Dtos.Ingest;

public sealed class IngestResponse
{
    public List<string> Accepted { get; set; } = new();
    public List<string> Duplicate { get; set; } = new();
    public List<IngestRejection> Rejected { get; set; } = new();
}

public sealed class IngestRejection
{
    public string BatchId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
