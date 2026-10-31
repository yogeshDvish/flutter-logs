using System.Threading.RateLimiting;

namespace FlutterLogs.Api.Infrastructure.Auth;

public static class RateLimitPartitions
{
    /// <summary>Fixed-window limiter partitioned by client IP address.</summary>
    public static RateLimitPartition<string> FixedWindow(
        HttpContext context,
        int permitLimit,
        TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(context);

        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    }
}
