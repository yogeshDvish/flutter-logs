namespace Arovyx.SealedTrace.Core.Entities;

public sealed class IngestBatch
{
    public Guid BatchId { get; init; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public DateTime ReceivedAtUtc { get; set; }

    public int EventCount { get; set; }

    public int EnvelopeSize { get; set; }

    public Project? Project { get; set; }

    public ICollection<LogEntry> Logs { get; } = new List<LogEntry>();
}
