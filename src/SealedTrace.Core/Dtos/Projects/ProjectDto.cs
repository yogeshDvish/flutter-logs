namespace Arovyx.SealedTrace.Core.Dtos.Projects;

public sealed class ProjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Environment { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public int? RetentionMaxAgeDays { get; set; }
    public long? RetentionMaxEvents { get; set; }
    public int ActiveApiKeyCount { get; set; }
    public int TodayEventCount { get; set; }
    public DateTime? LastReceivedAtUtc { get; set; }
}
