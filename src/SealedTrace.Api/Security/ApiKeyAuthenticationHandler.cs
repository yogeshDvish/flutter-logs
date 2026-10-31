using System.Security.Cryptography;
using System.Text;
using Arovyx.SealedTrace.Core.Entities;
using Arovyx.SealedTrace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arovyx.SealedTrace.Api.Security;

/// <summary>
/// Authenticates ingestion requests via the <c>X-Api-Key</c> header.
/// Hashes the header value, looks up the key, and attaches the project to the request.
/// </summary>
public sealed class ApiKeyAuthenticationHandler
{
    private const string HeaderName = "X-Api-Key";
    private const string ItemsKey = "SealedTrace.AuthenticatedProject";

    private readonly RequestDelegate _next;

    public ApiKeyAuthenticationHandler(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, SealedTraceDbContext db)
    {
        // Only enforce API key authentication on ingestion endpoints.
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api/v1/ingest", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var apiKeyHeader))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://sealedtrace.dev/errors/invalid_key",
                title = "Invalid key",
                status = 401,
                detail = "Missing X-Api-Key header.",
                code = "invalid_key",
                traceId = context.TraceIdentifier,
            });
            return;
        }

        var apiKeyValue = apiKeyHeader.ToString();
        if (string.IsNullOrWhiteSpace(apiKeyValue))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://sealedtrace.dev/errors/invalid_key",
                title = "Invalid key",
                status = 401,
                detail = "Missing X-Api-Key header.",
                code = "invalid_key",
                traceId = context.TraceIdentifier,
            });
            return;
        }

        var keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKeyValue.Trim()));

        var apiKey = await db.ApiKeys
            .Include(k => k.Project)
            .FirstOrDefaultAsync(k => k.KeyHash.SequenceEqual(keyHash));

        if (apiKey is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://sealedtrace.dev/errors/invalid_key",
                title = "Invalid key",
                status = 401,
                detail = "Unknown API key.",
                code = "invalid_key",
                traceId = context.TraceIdentifier,
            });
            return;
        }

        if (apiKey.RevokedAtUtc is not null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://sealedtrace.dev/errors/revoked_key",
                title = "Revoked key",
                status = 403,
                detail = "API key has been revoked.",
                code = "revoked_key",
                traceId = context.TraceIdentifier,
            });
            return;
        }

        // Attach the project to the request for downstream controllers.
        context.Items[ItemsKey] = apiKey.Project!;

        await _next(context);
    }

    public static Guid GetProjectId(HttpContext context)
    {
        if (context.Items.TryGetValue(ItemsKey, out var project) && project is Project p)
        {
            return p.Id;
        }

        throw new UnauthorizedAccessException("No authenticated project.");
    }
}
