using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace FlutterLogs.Api.Common;

/// <summary>
/// Converts empty 4xx responses (not found, method not allowed, unauthorized bodyless
/// responses) into RFC 7807 problem documents with a stable machine-readable code.
/// </summary>
public sealed class ProblemDetailsStatusCodeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        if (context.Response.HasStarted)
        {
            return;
        }

        var statusCode = context.Response.StatusCode;

        if (statusCode < 400 || statusCode >= 500)
        {
            return;
        }

        if (!string.IsNullOrEmpty(context.Response.ContentType))
        {
            return;
        }

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = ReasonPhrases.GetReasonPhrase(statusCode),
            Type = $"https://httpstatuses.io/{statusCode}",
            Instance = context.Request.Path,
            Extensions =
            {
                ["code"] = CodeFor(statusCode),
                ["traceId"] = context.TraceIdentifier,
            },
        };

        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json");
    }

    private static string CodeFor(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "bad_request",
        StatusCodes.Status401Unauthorized => "unauthorized",
        StatusCodes.Status403Forbidden => "forbidden",
        StatusCodes.Status404NotFound => "not_found",
        StatusCodes.Status405MethodNotAllowed => "method_not_allowed",
        StatusCodes.Status409Conflict => "conflict",
        StatusCodes.Status413PayloadTooLarge => "payload_too_large",
        StatusCodes.Status415UnsupportedMediaType => "unsupported_media_type",
        StatusCodes.Status429TooManyRequests => "rate_limited",
        _ => "request_failed",
    };
}
