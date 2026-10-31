using Arovyx.SealedTrace.Core.Dtos.Projects;

namespace Arovyx.SealedTrace.Core.Interfaces;

public interface IProjectService
{
    Task<(ProjectCreatedDto Created, string ApiKeyPlaintext)> CreateProjectAsync(
        Guid ownerId,
        CreateProjectRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectDto>> ListProjectsAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default);

    Task<ProjectDto?> GetProjectAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken = default);

    Task<ProjectDto> UpdateProjectAsync(
        Guid projectId,
        Guid ownerId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteProjectAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken = default);
}
