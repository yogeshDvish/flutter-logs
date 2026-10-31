namespace FlutterLogs.Api.Common;

/// <summary>Health payload returned by <c>GET /api/v1/health</c>.</summary>
public sealed record HealthResponse(string Status, string Db, string Version);

/// <summary>Build metadata exposed to clients.</summary>
public static class ApiInfo
{
    public static string Version { get; } =
        typeof(ApiInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
