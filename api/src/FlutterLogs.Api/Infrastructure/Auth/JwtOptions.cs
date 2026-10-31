using System.Text;

namespace FlutterLogs.Api.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "flutter-logs";

    public string Audience { get; set; } = "flutter-logs-web";

    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>
    /// Fails fast at startup rather than issuing tokens signed with a weak or
    /// missing key. The error intentionally does not echo the configured value.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SigningKey) || Encoding.UTF8.GetByteCount(SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is missing or shorter than 32 bytes. Configure it with " +
                "`dotnet user-secrets set \"Jwt:SigningKey\" \"<random 32+ byte value>\"` " +
                "or the Jwt__SigningKey environment variable.");
        }

        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience must be configured.");
        }

        if (AccessTokenMinutes is < 1 or > 120)
        {
            throw new InvalidOperationException("Jwt:AccessTokenMinutes must be between 1 and 120.");
        }
    }
}
