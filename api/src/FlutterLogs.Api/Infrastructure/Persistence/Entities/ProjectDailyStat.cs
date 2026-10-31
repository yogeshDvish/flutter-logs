namespace FlutterLogs.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// Per-project, per-UTC-day counters upserted inside the ingestion transaction so
/// the dashboard never has to scan the log table for totals.
/// </summary>
public sealed class ProjectDailyStat
{
    public Guid ProjectId { get; set; }

    public DateOnly Day { get; set; }

    public int LogCount { get; set; }

    /// <summary>Records at warning level or above.</summary>
    public int ErrorCount { get; set; }

    public long Bytes { get; set; }

    public Project? Project { get; set; }
}
