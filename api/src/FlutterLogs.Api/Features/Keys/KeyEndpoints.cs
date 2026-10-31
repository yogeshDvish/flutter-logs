using FlutterLogs.Api.Common;

namespace FlutterLogs.Api.Features.Keys;

public static class KeyEndpoints
{
    public static IEndpointRouteBuilder MapKeyEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup("/api/v1/projects/{projectId:guid}/keys")
            .WithTags("Keys")
            .RequireAuthorization();

        group.MapGet("", ListAsync)
            .WithName("ListProjectKeys")
            .WithSummary("Lists key metadata. Plaintext keys are never returned here.");

        group.MapPost("", CreateAsync)
            .WithName("CreateProjectKey")
            .WithSummary("Creates a key, or rotates by issuing a new one with a grace window.");

        group.MapDelete("/{keyId:guid}", RevokeAsync)
            .WithName("RevokeProjectKey")
            .WithSummary("Revokes a key immediately.");

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid projectId,
        HttpContext httpContext,
        KeyService keyService,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();

        if (userId is null)
        {
            return ApiResults.Unauthorized();
        }

        var result = await keyService.ListAsync(projectId, userId.Value, cancellationToken);

        return result.IsSuccess ? Results.Ok(result.Value) : ApiResults.Problem(result.Error!);
    }

    private static async Task<IResult> CreateAsync(
        Guid projectId,
        CreateProjectKeyRequest request,
        CreateProjectKeyRequestValidator validator,
        HttpContext httpContext,
        KeyService keyService,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();

        if (userId is null)
        {
            return ApiResults.Unauthorized();
        }

        var errors = validator.ValidateToDictionary(request);

        if (errors is not null)
        {
            return ApiResults.ValidationProblem(errors);
        }

        var result = await keyService.CreateAsync(
            projectId,
            userId.Value,
            request,
            cancellationToken);

        return result.IsSuccess
            ? Results.Created($"/api/v1/projects/{projectId}/keys", result.Value)
            : ApiResults.Problem(result.Error!);
    }

    private static async Task<IResult> RevokeAsync(
        Guid projectId,
        Guid keyId,
        HttpContext httpContext,
        KeyService keyService,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();

        if (userId is null)
        {
            return ApiResults.Unauthorized();
        }

        var result = await keyService.RevokeAsync(
            projectId,
            keyId,
            userId.Value,
            cancellationToken);

        return result.IsSuccess ? Results.NoContent() : ApiResults.Problem(result.Error!);
    }
}
