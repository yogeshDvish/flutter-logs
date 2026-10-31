namespace FlutterLogs.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// Security-relevant action trail. Never contains secrets, tokens, or log bodies.
/// Rows outlive the project they reference, so there is no foreign key.
/// </summary>
public sealed class AuditEvent
{
    public long Id { get; init; }

    public Guid? UserId { get; set; }

    public Guid? ProjectId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? TargetType { get; set; }

    public string? TargetId { get; set; }

    /// <summary>JSON object with non-sensitive context only.</summary>
    public string? Metadata { get; set; }

    public DateTime CreatedAt { get; set; }
}
