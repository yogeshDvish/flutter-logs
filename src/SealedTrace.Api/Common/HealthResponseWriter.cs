using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Arovyx.SealedTrace.Api.Common;

public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var checks = report.Entries.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Status.ToString(),
            StringComparer.Ordinal);

        var payload = new HealthResponse(report.Status.ToString(), checks);

        return context.Response.WriteAsJsonAsync(payload, context.RequestAborted);
    }

    public sealed record HealthResponse(
        string Status,
        IReadOnlyDictionary<string, string> Checks);
}
