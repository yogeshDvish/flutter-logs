using System.Collections.Concurrent;
using FlutterLogs.Api.Infrastructure.Email;

namespace FlutterLogs.Api.Tests;

/// <summary>Captures dev-mode email links so tests can drive token flows end to end.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentDictionary<string, string> verificationLinks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, string> resetLinks =
        new(StringComparer.OrdinalIgnoreCase);

    public Task SendVerificationAsync(
        string email,
        string link,
        CancellationToken cancellationToken)
    {
        verificationLinks[email] = link;
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(
        string email,
        string link,
        CancellationToken cancellationToken)
    {
        resetLinks[email] = link;
        return Task.CompletedTask;
    }

    public string? GetVerificationLink(string email) =>
        verificationLinks.GetValueOrDefault(email);

    public string? GetResetLink(string email) =>
        resetLinks.GetValueOrDefault(email);

    public string? GetVerificationToken(string email) =>
        ExtractToken(GetVerificationLink(email));

    public string? GetResetToken(string email) =>
        ExtractToken(GetResetLink(email));

    private static string? ExtractToken(string? link)
    {
        if (link is null)
        {
            return null;
        }

        var queryStart = link.IndexOf('?', StringComparison.Ordinal);

        if (queryStart < 0)
        {
            return null;
        }

        foreach (var pair in link[(queryStart + 1)..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);

            if (parts.Length == 2 && parts[0] == "token")
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }
}
