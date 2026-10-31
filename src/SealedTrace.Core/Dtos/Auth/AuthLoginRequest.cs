namespace Arovyx.SealedTrace.Core.Dtos.Auth;

public sealed class AuthLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
