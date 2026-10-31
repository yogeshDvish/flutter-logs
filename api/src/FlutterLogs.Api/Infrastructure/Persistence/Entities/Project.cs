namespace FlutterLogs.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// A container for one application's logs. The immutable <see cref="Id"/> routes
/// ingestion and queries; the name is a label only and is never an identifier.
/// </summary>
public sealed class Project
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid OwnerUserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IngestEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Soft-delete marker; archived projects are hidden from the default list.</summary>
    public DateTime? ArchivedAt { get; set; }

    public User? Owner { get; set; }

    public ICollection<ProjectKey> Keys { get; } = new List<ProjectKey>();

    public ICollection<ProjectDailyStat> DailyStats { get; } = new List<ProjectDailyStat>();
}
