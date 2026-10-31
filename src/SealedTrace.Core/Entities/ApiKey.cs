namespace Arovyx.SealedTrace.Core.Entities;

public sealed class ApiKey
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    public byte[] KeyHash { get; set; } = [];

    public string KeyPrefix { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public Project? Project { get; set; }
}
