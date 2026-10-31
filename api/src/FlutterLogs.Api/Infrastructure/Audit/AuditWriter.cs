using System.Text.Json;
using FlutterLogs.Api.Infrastructure.Persistence;
using FlutterLogs.Api.Infrastructure.Persistence.Entities;

namespace FlutterLogs.Api.Infrastructure.Audit;

public static class AuditActions
{
    public const string ProjectCreate = "project.create";
    public const string ProjectUpdate = "project.update";
    public const string ProjectArchive = "project.archive";
    public const string ProjectPurge = "project.purge";
    public const string KeyCreate = "key.create";
    public const string KeyRotate = "key.rotate";
    public const string KeyRevoke = "key.revoke";
}

/// <summary>
/// Adds audit rows to the same unit of work as the action they describe. Callers
/// save once, so an action and its audit record commit atomically.
/// </summary>
public sealed class AuditWriter(AppDbContext db, TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public void Add(
        Guid? userId,
        Guid? projectId,
        string action,
        string? targetType = null,
        string? targetId = null,
        object? metadata = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            UserId = userId,
            ProjectId = projectId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Metadata = metadata is null ? null : JsonSerializer.Serialize(metadata, SerializerOptions),
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        });
    }
}
