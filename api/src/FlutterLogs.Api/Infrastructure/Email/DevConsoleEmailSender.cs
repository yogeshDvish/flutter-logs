using FlutterLogs.Api.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace FlutterLogs.Api.Infrastructure.Email;

public interface IEmailSender
{
    Task SendVerificationAsync(string email, string link, CancellationToken cancellationToken);

    Task SendPasswordResetAsync(string email, string link, CancellationToken cancellationToken);
}

/// <summary>
/// Development stand-in for email delivery. Links are written to the structured log
/// only when <c>Auth:DevEmailMode</c> is enabled; otherwise nothing sensitive is logged.
/// A real provider arrives in a later phase behind the same interface.
/// </summary>
public sealed partial class DevConsoleEmailSender(
    IOptions<AuthOptions> options,
    ILogger<DevConsoleEmailSender> logger) : IEmailSender
{
    public Task SendVerificationAsync(string email, string link, CancellationToken cancellationToken)
    {
        Log("verification", email, link);
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string email, string link, CancellationToken cancellationToken)
    {
        Log("password-reset", email, link);
        return Task.CompletedTask;
    }

    private void Log(string kind, string email, string link)
    {
        if (options.Value.DevEmailMode)
        {
            LogDevEmail(logger, kind, email, link);
            return;
        }

        LogNotConfigured(logger, kind, email);
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "[DEV EMAIL] {Kind} link for {Email}: {Link}")]
    private static partial void LogDevEmail(ILogger logger, string kind, string email, string link);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Email delivery is not configured; the {Kind} email for {Email} was not sent.")]
    private static partial void LogNotConfigured(ILogger logger, string kind, string email);
}
