namespace Arovyx.SealedTrace.Core.Entities;

public sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public byte[]? ReplacedByTokenHash { get; set; }

    public User? User { get; set; }
}
