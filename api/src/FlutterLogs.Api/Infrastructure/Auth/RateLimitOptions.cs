namespace FlutterLogs.Api.Infrastructure.Auth;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public int RegisterPerHour { get; set; } = 5;

    public int LoginPerMinute { get; set; } = 10;

    public int ForgotPasswordPerHour { get; set; } = 5;
}
