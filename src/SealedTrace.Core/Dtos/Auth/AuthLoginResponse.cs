namespace Arovyx.SealedTrace.Core.Dtos.Auth;

public sealed class AuthLoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = "Bearer";
    public int ExpiresIn { get; set; }
    public string? RefreshTokenPlaintext { get; set; }
    public AuthUserDto User { get; set; } = new();
}

public sealed class AuthUserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
}
