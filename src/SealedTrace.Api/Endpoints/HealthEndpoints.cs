using Arovyx.SealedTrace.Api.Common;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Arovyx.SealedTrace.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
            .WithName("Liveness")
            .WithTags("Health")
            .AllowAnonymous();

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = HealthResponseWriter.WriteAsync,
        })
            .WithName("Readiness")
            .WithTags("Health")
            .AllowAnonymous();

        return endpoints;
    }
}
