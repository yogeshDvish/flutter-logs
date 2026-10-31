using Arovyx.SealedTrace.Core.Dtos.ApiKeys;

namespace Arovyx.SealedTrace.Core.Dtos.Projects;

public sealed class ProjectCreatedDto
{
    public ProjectDto Project { get; set; } = new();
    public ApiKeyCreatedDto ApiKey { get; set; } = new();
    public string PublicKeyPem { get; set; } = string.Empty;
}
