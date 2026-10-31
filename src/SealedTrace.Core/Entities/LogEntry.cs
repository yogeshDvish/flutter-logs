namespace Arovyx.SealedTrace.Core.Entities;

public sealed class LogEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public Guid BatchId { get; set; }

    public DateTime ClientTs { get; set; }

    public DateTime ReceivedAtUtc { get; set; }

    public SealedTraceLevel Level { get; set; }

    public string Tag { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Error { get; set; }

    public string? StackTrace { get; set; }

    public string? MetadataJson { get; set; }

    public Guid? SessionId { get; set; }

    public string? UserId { get; set; }

    public long Seq { get; set; }

    public string? AppVersion { get; set; }

    public string? Platform { get; set; }

    public Project? Project { get; set; }

    public IngestBatch? Batch { get; set; }
}
