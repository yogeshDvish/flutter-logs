using Serilog.Context;

namespace Arovyx.SealedTrace.Api.Common;

/// <summary>
/// Accepts or generates a correlation id, echoes it on the response, and pushes it
/// into the Serilog context so every log line for a request can be traced.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Resolve(context);

        context.TraceIdentifier = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static string Resolve(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();

        if (!string.IsNullOrWhiteSpace(incoming)
            && incoming.Length <= MaxLength
            && incoming.All(IsAllowedCharacter))
        {
            return incoming;
        }

        return Guid.NewGuid().ToString("N");
    }

    // Rejects anything that could break a header or a log line.
    private static bool IsAllowedCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.';
}
