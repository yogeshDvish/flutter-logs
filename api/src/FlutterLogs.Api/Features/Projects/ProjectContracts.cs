namespace FlutterLogs.Api.Features.Projects;

public sealed record CreateProjectRequest(string Name, string? Description);

public sealed record UpdateProjectRequest(
    string? Name,
    string? Description,
    bool? IngestEnabled);

public sealed record ProjectStatsBucket(int Total, int Errors);

public sealed record ProjectStatsDto(
    ProjectStatsBucket Last24Hours,
    ProjectStatsBucket Last7Days,
    DateTime? LastLogAt);

public sealed record ProjectSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    bool IngestEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ArchivedAt,
    int ActiveKeyCount,
    ProjectStatsDto Stats);

/// <summary>
/// Returned once when a project is created. <c>Key.Plaintext</c> is the only time
/// the full ingestion key is available.
/// </summary>
public sealed record ProjectCreatedResponse(
    ProjectSummaryDto Project,
    IssuedProjectKeyDto Key);

public sealed record IssuedProjectKeyDto(
    Guid Id,
    string Prefix,
    string? Label,
    DateTime CreatedAt,
    DateTime? GraceUntil,
    string Plaintext);

public sealed record ProjectStatsResponse(
    Guid ProjectId,
    ProjectStatsBucket Last24Hours,
    ProjectStatsBucket Last7Days,
    DateTime? LastLogAt);
