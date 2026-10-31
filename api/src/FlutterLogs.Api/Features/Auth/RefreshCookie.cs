using FlutterLogs.Api.Infrastructure.Auth;

namespace FlutterLogs.Api.Features.Auth;

/// <summary>
/// Writes the refresh token as an <c>httpOnly</c>, <c>SameSite=Strict</c> cookie scoped
/// to the auth endpoints, so it is never exposed to JavaScript.
/// </summary>
public static class RefreshCookie
{
    public static void Write(
        HttpContext context,
        AuthOptions options,
        string token,
        DateTime expiresAt)
    {
        context.Response.Cookies.Append(options.CookieName, token, BuildOptions(options, expiresAt));
    }

    public static string? Read(HttpContext context, AuthOptions options) =>
        context.Request.Cookies[options.CookieName];

    public static void Delete(HttpContext context, AuthOptions options)
    {
        var cookieOptions = BuildOptions(options, expiresAt: null);
        cookieOptions.Expires = DateTimeOffset.UnixEpoch;
        context.Response.Cookies.Delete(options.CookieName, cookieOptions);
    }

    private static CookieOptions BuildOptions(AuthOptions options, DateTime? expiresAt) =>
        new()
        {
            HttpOnly = true,
            Secure = options.CookieSecure,
            SameSite = SameSiteMode.Strict,
            Path = options.CookiePath,
            IsEssential = true,
            Expires = expiresAt is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(expiresAt.Value, DateTimeKind.Utc)),
        };
}
