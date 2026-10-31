namespace Arovyx.SealedTrace.Core.Entities;

public sealed class Project
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid OwnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Environment { get; set; } = "prod";

    public DateTime CreatedAtUtc { get; set; }

    public int? RetentionMaxAgeDays { get; set; }

    public long? RetentionMaxEvents { get; set; }

    public User? Owner { get; set; }

    public ICollection<ApiKey> ApiKeys { get; } = new List<ApiKey>();

    public ProjectKey? ProjectKey { get; set; }

    public ICollection<IngestBatch> IngestBatches { get; } = new List<IngestBatch>();

    public ICollection<LogEntry> Logs { get; } = new List<LogEntry>();
}
