namespace Arovyx.SealedTrace.Api.Common;

public static class ApiInfo
{
    public static string Version =>
        typeof(ApiInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
