using FlutterLogs.Api.Common;
using FlutterLogs.Api.Infrastructure.Audit;
using FlutterLogs.Api.Infrastructure.Auth;
using FlutterLogs.Api.Infrastructure.Persistence;
using FlutterLogs.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FlutterLogs.Api.Features.Keys;

public sealed partial class KeyService(
    AppDbContext db,
    IngestionKeyIssuer keyIssuer,
    AuditWriter auditWriter,
    IOptions<IngestOptions> ingestOptions,
    TimeProvider timeProvider,
    ILogger<KeyService> logger)
{
    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<OperationResult<IReadOnlyList<ProjectKeyDto>>> ListAsync(
        Guid projectId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects
            .AsNoTracking()
            .AnyAsync(
                project => project.Id == projectId && project.OwnerUserId == userId,
                cancellationToken);

        if (!projectExists)
        {
            return NotFound<IReadOnlyList<ProjectKeyDto>>();
        }

        var now = Now;

        var keys = await db.ProjectKeys
            .AsNoTracking()
            .Where(key => key.ProjectId == projectId)
            .OrderByDescending(key => key.CreatedAt)
            .ToListAsync(cancellationToken);

        return OperationResults.Ok<IReadOnlyList<ProjectKeyDto>>(
            keys.Select(key => ToDto(key, now)).ToArray());
    }

    public async Task<OperationResult<IssuedKeyResponse>> CreateAsync(
        Guid projectId,
        Guid userId,
        CreateProjectKeyRequest request,
        CancellationToken cancellationToken)
    {
        var project = await db.Projects.FindOwnedAsync(projectId, userId, cancellationToken);

        if (project is null)
        {
            return NotFound<IssuedKeyResponse>();
        }

        if (project.ArchivedAt is not null)
        {
            return OperationResults.Fail<IssuedKeyResponse>(new ApiError(
                "project_archived",
                "Archived projects cannot issue ingestion keys.",
                StatusCodes.Status409Conflict));
        }

        var now = Now;
        var options = ingestOptions.Value;

        var activeKeys = await db.ProjectKeys
            .Where(key => key.ProjectId == projectId
                && key.RevokedAt == null
                && (key.GraceUntil == null || key.GraceUntil > now))
            .OrderBy(key => key.CreatedAt)
            .ToListAsync(cancellationToken);

        if (activeKeys.Count >= options.MaxKeysPerProject)
        {
            return OperationResults.Fail<IssuedKeyResponse>(new ApiError(
                "key_limit_reached",
                $"A project can have at most {options.MaxKeysPerProject} active keys. Revoke one before rotating.",
                StatusCodes.Status409Conflict));
        }

        var graceHours = request.GraceHours ?? options.RotationGraceHours;
        var graceUntil = graceHours > 0 ? now.AddHours(graceHours) : (DateTime?)null;
        var isRotation = activeKeys.Count > 0;

        foreach (var activeKey in activeKeys)
        {
            if (graceHours == 0)
            {
                activeKey.RevokedAt = now;
            }
            else if (activeKey.GraceUntil is null || activeKey.GraceUntil > graceUntil)
            {
                activeKey.GraceUntil = graceUntil;
            }
        }

        var issued = await keyIssuer.IssueAsync(cancellationToken);

        var key = new ProjectKey
        {
            ProjectId = projectId,
            Prefix = issued.Prefix,
            KeyHash = issued.Hash,
            Label = NormalizeLabel(request.Label),
            CreatedAt = now,
            GraceUntil = null,
        };

        db.ProjectKeys.Add(key);

        auditWriter.Add(
            userId,
            projectId,
            isRotation ? AuditActions.KeyRotate : AuditActions.KeyCreate,
            "project_key",
            key.Id.ToString(),
            new
            {
                prefix = key.Prefix,
                superseded = activeKeys.Select(active => active.Prefix).ToArray(),
                graceHours,
            });

        await db.SaveChangesAsync(cancellationToken);

        LogKeyIssued(logger, key.Id, projectId, userId, isRotation);

        return OperationResults.Ok(new IssuedKeyResponse(
            key.Id,
            key.Prefix,
            key.Label,
            AsUtc(key.CreatedAt),
            null,
            issued.Plaintext));
    }

    public async Task<OperationResult<bool>> RevokeAsync(
        Guid projectId,
        Guid keyId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects
            .AsNoTracking()
            .AnyAsync(
                project => project.Id == projectId && project.OwnerUserId == userId,
                cancellationToken);

        if (!projectExists)
        {
            return NotFound<bool>();
        }

        var key = await db.ProjectKeys.SingleOrDefaultAsync(
            candidate => candidate.Id == keyId && candidate.ProjectId == projectId,
            cancellationToken);

        if (key is null)
        {
            return OperationResults.Fail<bool>(new ApiError(
                "not_found",
                "Key not found.",
                StatusCodes.Status404NotFound));
        }

        if (key.RevokedAt is null)
        {
            key.RevokedAt = Now;

            auditWriter.Add(
                userId,
                projectId,
                AuditActions.KeyRevoke,
                "project_key",
                key.Id.ToString(),
                new { prefix = key.Prefix });

            await db.SaveChangesAsync(cancellationToken);

            LogKeyRevoked(logger, key.Id, projectId, userId);
        }

        return OperationResults.Ok(true);
    }

    private static ProjectKeyDto ToDto(ProjectKey key, DateTime now)
    {
        var status = key.RevokedAt is not null
            ? "revoked"
            : key.GraceUntil is null
                ? "active"
                : key.GraceUntil <= now
                    ? "expired"
                    : "grace";

        return new ProjectKeyDto(
            key.Id,
            key.Prefix,
            key.Label,
            status,
            AsUtc(key.CreatedAt),
            AsUtc(key.LastUsedAt),
            AsUtc(key.GraceUntil),
            AsUtc(key.RevokedAt));
    }

    private static string? NormalizeLabel(string? label) =>
        string.IsNullOrWhiteSpace(label) ? null : label.Trim();

    private static DateTime AsUtc(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value) =>
        value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    private static OperationResult<T> NotFound<T>() =>
        OperationResults.Fail<T>(new ApiError(
            "not_found",
            "Project not found.",
            StatusCodes.Status404NotFound));

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information, Message = "Key {KeyId} issued for project {ProjectId} by account {UserId} (rotation: {IsRotation}).")]
    private static partial void LogKeyIssued(
        ILogger logger,
        Guid keyId,
        Guid projectId,
        Guid userId,
        bool isRotation);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Warning, Message = "Key {KeyId} revoked for project {ProjectId} by account {UserId}.")]
    private static partial void LogKeyRevoked(ILogger logger, Guid keyId, Guid projectId, Guid userId);
}
