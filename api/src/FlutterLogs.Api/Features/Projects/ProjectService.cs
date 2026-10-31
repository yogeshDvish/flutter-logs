using FlutterLogs.Api.Common;
using FlutterLogs.Api.Infrastructure.Audit;
using FlutterLogs.Api.Infrastructure.Auth;
using FlutterLogs.Api.Infrastructure.Persistence;
using FlutterLogs.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FlutterLogs.Api.Features.Projects;

public sealed partial class ProjectService(
    AppDbContext db,
    AuditWriter auditWriter,
    IngestionKeyIssuer keyIssuer,
    IOptions<ProjectsOptions> projectOptions,
    TimeProvider timeProvider,
    ILogger<ProjectService> logger)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<ProjectSummaryDto>> ListAsync(
        Guid userId,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var query = db.Projects
            .AsNoTracking()
            .OwnedBy(userId);

        if (!includeArchived)
        {
            query = query.Where(project => project.ArchivedAt == null);
        }

        var projects = await query
            .OrderBy(project => project.ArchivedAt != null)
            .ThenByDescending(project => project.CreatedAt)
            .ToListAsync(cancellationToken);

        return await BuildSummariesAsync(projects, cancellationToken);
    }

    public async Task<OperationResult<ProjectSummaryDto>> GetAsync(
        Guid projectId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == projectId && candidate.OwnerUserId == userId,
                cancellationToken);

        if (project is null)
        {
            return NotFound<ProjectSummaryDto>();
        }

        var summaries = await BuildSummariesAsync([project], cancellationToken);

        return OperationResults.Ok(summaries[0]);
    }

    public async Task<OperationResult<ProjectCreatedResponse>> CreateAsync(
        Guid userId,
        CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var activeProjectCount = await db.Projects
            .CountAsync(
                project => project.OwnerUserId == userId && project.ArchivedAt == null,
                cancellationToken);

        if (activeProjectCount >= projectOptions.Value.MaxPerUser)
        {
            return OperationResults.Fail<ProjectCreatedResponse>(new ApiError(
                "project_limit_reached",
                $"An account can own at most {projectOptions.Value.MaxPerUser} active projects.",
                StatusCodes.Status409Conflict));
        }

        var now = Now;
        var project = new Project
        {
            OwnerUserId = userId,
            Name = request.Name.Trim(),
            Description = NormalizeDescription(request.Description),
            IngestEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var issued = await keyIssuer.IssueAsync(cancellationToken);
        var key = new ProjectKey
        {
            ProjectId = project.Id,
            Prefix = issued.Prefix,
            KeyHash = issued.Hash,
            CreatedAt = now,
        };

        db.Projects.Add(project);
        db.ProjectKeys.Add(key);

        auditWriter.Add(userId, project.Id, AuditActions.ProjectCreate, "project", project.Id.ToString());
        auditWriter.Add(
            userId,
            project.Id,
            AuditActions.KeyCreate,
            "project_key",
            key.Id.ToString(),
            new { prefix = key.Prefix });

        await db.SaveChangesAsync(cancellationToken);

        LogProjectCreated(logger, project.Id, userId);

        var summary = new ProjectSummaryDto(
            project.Id,
            project.Name,
            project.Description,
            project.IngestEnabled,
            AsUtc(project.CreatedAt),
            AsUtc(project.UpdatedAt),
            null,
            1,
            EmptyStats());

        return OperationResults.Ok(new ProjectCreatedResponse(
            summary,
            new IssuedProjectKeyDto(
                key.Id,
                key.Prefix,
                key.Label,
                AsUtc(key.CreatedAt),
                null,
                issued.Plaintext)));
    }

    public async Task<OperationResult<ProjectSummaryDto>> UpdateAsync(
        Guid projectId,
        Guid userId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects.FindOwnedAsync(projectId, userId, cancellationToken);

        if (project is null)
        {
            return NotFound<ProjectSummaryDto>();
        }

        if (project.ArchivedAt is not null)
        {
            return Archived<ProjectSummaryDto>();
        }

        var changedFields = new List<string>();

        if (request.Name is not null)
        {
            project.Name = request.Name.Trim();
            changedFields.Add("name");
        }

        if (request.Description is not null)
        {
            project.Description = NormalizeDescription(request.Description);
            changedFields.Add("description");
        }

        if (request.IngestEnabled is not null && request.IngestEnabled != project.IngestEnabled)
        {
            project.IngestEnabled = request.IngestEnabled.Value;
            changedFields.Add("ingestEnabled");
        }

        project.UpdatedAt = Now;

        auditWriter.Add(
            userId,
            project.Id,
            AuditActions.ProjectUpdate,
            "project",
            project.Id.ToString(),
            new { fields = changedFields });

        await db.SaveChangesAsync(cancellationToken);

        var summaries = await BuildSummariesAsync([project], cancellationToken);

        return OperationResults.Ok(summaries[0]);
    }

    public async Task<OperationResult<bool>> DeleteAsync(
        Guid projectId,
        Guid userId,
        bool purge,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects.FindOwnedAsync(projectId, userId, cancellationToken);

        if (project is null)
        {
            return NotFound<bool>();
        }

        if (purge)
        {
            auditWriter.Add(userId, project.Id, AuditActions.ProjectPurge, "project", project.Id.ToString());

            // Audit insert and project delete share SaveChanges' implicit transaction.
            db.Projects.Remove(project);
            await db.SaveChangesAsync(cancellationToken);

            LogProjectPurged(logger, projectId, userId);

            return OperationResults.Ok(true);
        }

        if (project.ArchivedAt is null)
        {
            var now = Now;
            project.ArchivedAt = now;
            project.IngestEnabled = false;
            project.UpdatedAt = now;

            auditWriter.Add(
                userId,
                project.Id,
                AuditActions.ProjectArchive,
                "project",
                project.Id.ToString());

            await db.SaveChangesAsync(cancellationToken);

            LogProjectArchived(logger, project.Id, userId);
        }

        return OperationResults.Ok(true);
    }

    public async Task<OperationResult<ProjectStatsResponse>> GetStatsAsync(
        Guid projectId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var exists = await db.Projects
            .AsNoTracking()
            .AnyAsync(
                project => project.Id == projectId && project.OwnerUserId == userId,
                cancellationToken);

        if (!exists)
        {
            return NotFound<ProjectStatsResponse>();
        }

        var stats = (await LoadStatsAsync([projectId], cancellationToken))[projectId];

        return OperationResults.Ok(new ProjectStatsResponse(
            projectId,
            stats.Last24Hours,
            stats.Last7Days,
            stats.LastLogAt));
    }

    private async Task<IReadOnlyList<ProjectSummaryDto>> BuildSummariesAsync(
        List<Project> projects,
        CancellationToken cancellationToken)
    {
        if (projects.Count == 0)
        {
            return [];
        }

        var projectIds = projects.Select(project => project.Id).ToArray();
        var stats = await LoadStatsAsync(projectIds, cancellationToken);
        var keyCounts = await LoadActiveKeyCountsAsync(projectIds, cancellationToken);

        return projects
            .Select(project => new ProjectSummaryDto(
                project.Id,
                project.Name,
                project.Description,
                project.IngestEnabled,
                AsUtc(project.CreatedAt),
                AsUtc(project.UpdatedAt),
                AsUtc(project.ArchivedAt),
                keyCounts.GetValueOrDefault(project.Id),
                stats[project.Id]))
            .ToArray();
    }

    private async Task<Dictionary<Guid, ProjectStatsDto>> LoadStatsAsync(
        Guid[] projectIds,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(Now);
        var firstDay = today.AddDays(-6);

        var rows = await db.ProjectDailyStats
            .AsNoTracking()
            .Where(stat => projectIds.Contains(stat.ProjectId) && stat.Day >= firstDay)
            .ToListAsync(cancellationToken);

        return projectIds.ToDictionary(
            projectId => projectId,
            projectId =>
            {
                var projectRows = rows.Where(row => row.ProjectId == projectId).ToArray();
                var todayRows = projectRows.Where(row => row.Day == today).ToArray();

                return new ProjectStatsDto(
                    new ProjectStatsBucket(
                        todayRows.Sum(row => row.LogCount),
                        todayRows.Sum(row => row.ErrorCount)),
                    new ProjectStatsBucket(
                        projectRows.Sum(row => row.LogCount),
                        projectRows.Sum(row => row.ErrorCount)),
                    null);
            });
    }

    private async Task<Dictionary<Guid, int>> LoadActiveKeyCountsAsync(
        Guid[] projectIds,
        CancellationToken cancellationToken)
    {
        var now = Now;

        var counts = await db.ProjectKeys
            .AsNoTracking()
            .Where(key => projectIds.Contains(key.ProjectId)
                && key.RevokedAt == null
                && (key.GraceUntil == null || key.GraceUntil > now))
            .GroupBy(key => key.ProjectId)
            .Select(group => new { ProjectId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(entry => entry.ProjectId, entry => entry.Count);
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static DateTime AsUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    private static ProjectStatsDto EmptyStats() =>
        new(new ProjectStatsBucket(0, 0), new ProjectStatsBucket(0, 0), null);

    private static OperationResult<T> NotFound<T>() =>
        OperationResults.Fail<T>(new ApiError(
            "not_found",
            "Project not found.",
            StatusCodes.Status404NotFound));

    private static OperationResult<T> Archived<T>() =>
        OperationResults.Fail<T>(new ApiError(
            "project_archived",
            "This project is archived. Restore is not available in the MVP.",
            StatusCodes.Status409Conflict));

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Project {ProjectId} created by account {UserId}.")]
    private static partial void LogProjectCreated(ILogger logger, Guid projectId, Guid userId);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Project {ProjectId} archived by account {UserId}.")]
    private static partial void LogProjectArchived(ILogger logger, Guid projectId, Guid userId);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning, Message = "Project {ProjectId} purged by account {UserId}.")]
    private static partial void LogProjectPurged(ILogger logger, Guid projectId, Guid userId);
}
