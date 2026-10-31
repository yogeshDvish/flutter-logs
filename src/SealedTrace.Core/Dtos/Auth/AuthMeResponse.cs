namespace Arovyx.SealedTrace.Core.Dtos.Auth;

public sealed class AuthMeResponse
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
}
