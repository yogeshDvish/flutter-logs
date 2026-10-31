namespace FlutterLogs.Api.Infrastructure.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public int RefreshTokenDays { get; set; } = 7;

    public int EmailVerificationHours { get; set; } = 24;

    public int PasswordResetHours { get; set; } = 1;

    public int PasswordMinLength { get; set; } = 12;

    public int PasswordMaxLength { get; set; } = 128;

    /// <summary>Must be false only for plain-HTTP local development.</summary>
    public bool CookieSecure { get; set; } = true;

    /// <summary>When true, verification and reset links are written to the server log.</summary>
    public bool DevEmailMode { get; set; }

    /// <summary>Base URL used to build links in emails.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5173";

    public string CookieName { get; set; } = "fl_refresh";

    public string CookiePath { get; set; } = "/api/v1/auth";
}
