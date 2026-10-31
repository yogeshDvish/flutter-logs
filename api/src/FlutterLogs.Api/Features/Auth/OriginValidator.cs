using FlutterLogs.Api.Infrastructure.Auth;

namespace FlutterLogs.Api.Features.Auth;

/// <summary>
/// CSRF defence in depth for cookie-authenticated endpoints. Browsers send an
/// <c>Origin</c> header on cross-site POSTs; non-browser clients usually do not.
/// A request is accepted when the origin is same-origin or matches the configured
/// dashboard origin (<c>Auth:PublicBaseUrl</c>).
/// </summary>
public static class OriginValidator
{
    public static bool IsAllowed(HttpContext context, AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var origin = context.Request.Headers.Origin.ToString();

        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        var requestOrigin = $"{context.Request.Scheme}://{context.Request.Host}";

        if (string.Equals(origin.TrimEnd('/'), requestOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
            || !Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var publicUri))
        {
            return false;
        }

        return originUri.Scheme == publicUri.Scheme
            && string.Equals(originUri.Host, publicUri.Host, StringComparison.OrdinalIgnoreCase)
            && originUri.Port == publicUri.Port;
    }
}
