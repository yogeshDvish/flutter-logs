namespace Arovyx.SealedTrace.Core.Dtos.Projects;

public sealed class UpdateProjectRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Environment { get; set; }
    public int? RetentionMaxAgeDays { get; set; }
    public long? RetentionMaxEvents { get; set; }
}
