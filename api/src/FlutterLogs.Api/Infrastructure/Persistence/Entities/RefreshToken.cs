namespace FlutterLogs.Api.Infrastructure.Persistence.Entities;

/// <summary>
/// One rotation step of a refresh-token family. Only the SHA-256 hash of the
/// opaque token is stored; reuse of a consumed token revokes the whole family.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public Guid FamilyId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public Guid? ReplacedById { get; set; }

    public User? User { get; set; }
}
