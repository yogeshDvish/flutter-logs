namespace Arovyx.SealedTrace.Core.Dtos.ApiKeys;

public sealed class ApiKeyCreatedDto
{
    public Guid Id { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public string Plaintext { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
