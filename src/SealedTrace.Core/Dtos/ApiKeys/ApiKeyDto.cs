namespace Arovyx.SealedTrace.Core.Dtos.ApiKeys;

public sealed class ApiKeyDto
{
    public Guid Id { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public bool IsRevoked => RevokedAtUtc is not null;
}
