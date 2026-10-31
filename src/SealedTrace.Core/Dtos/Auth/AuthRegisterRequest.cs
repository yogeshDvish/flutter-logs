namespace Arovyx.SealedTrace.Core.Dtos.Auth;

public sealed class AuthRegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Name { get; set; }
}
