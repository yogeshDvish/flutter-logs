namespace Arovyx.SealedTrace.Core.Dtos.Projects;

public sealed class CreateProjectRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Environment { get; set; } = "prod";
}
