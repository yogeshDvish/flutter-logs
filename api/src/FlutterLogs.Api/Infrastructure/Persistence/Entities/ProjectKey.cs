namespace FlutterLogs.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// A write-only credential scoped to one project. Only the non-secret prefix and
/// an HMAC-SHA256 digest are stored; the plaintext is shown once at issue time.
/// </summary>
public sealed class ProjectKey
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid ProjectId { get; set; }

    /// <summary>Eight non-secret characters used for indexed lookup.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 of the secret part, keyed with the server pepper.</summary>
    public byte[] KeyHash { get; set; } = [];

    public string? Label { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Updated at most once per minute by ingestion (Phase 4).</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Rotation window during which the superseded key still works (max 24 h).</summary>
    public DateTime? GraceUntil { get; set; }

    public DateTime? RevokedAt { get; set; }

    public Project? Project { get; set; }
}
