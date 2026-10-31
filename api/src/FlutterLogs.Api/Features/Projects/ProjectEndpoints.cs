using FlutterLogs.Api.Common;

namespace FlutterLogs.Api.Features.Projects;

public static class ProjectEndpoints
{
    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup("/api/v1/projects")
            .WithTags("Projects")
            .RequireAuthorization();

        group.MapGet("", ListAsync)
            .WithName("ListProjects")
            .WithSummary("Lists the caller's projects with key counts and log statistics.");

        group.MapPost("", CreateAsync)
            .WithName("CreateProject")
            .WithSummary("Creates a project and issues its first ingestion key (shown once).");

        group.MapGet("/{projectId:guid}", GetAsync)
            .WithName("GetProject");

        group.MapPatch("/{projectId:guid}", UpdateAsync)
            .WithName("UpdateProject");

        group.MapDelete("/{projectId:guid}", DeleteAsync)
            .WithName("DeleteProject")
            .WithSummary("Archives a project, or permanently deletes it with ?purge=true.");

        group.MapGet("/{projectId:guid}/stats", GetStatsAsync)
            .WithName("GetProjectStats");

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        ProjectService projectService,
        bool? includeArchived,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();

        if (userId is null)
        {
            return ApiResults.Unauthorized();
        }

        var projects = await projectService.ListAsync(
            userId.Value,
            includeArchived ?? false,
            cancellationToken);

        return Results.Ok(projects);
    }

    private static async Task<IResult> CreateAsync(
        CreateProjectRequest request,
        CreateProjectRequestValidator validator,
        HttpContext httpContext,
        ProjectService projectService,
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

        var result = await projectService.CreateAsync(userId.Value, request, cancellationToken);

        return result.IsSuccess
            ? Results.Created($"/api/v1/projects/{result.Value!.Project.Id}", result.Value)
            : ApiResults.Problem(result.Error!);
    }

    private static async Task<IResult> GetAsync(
        Guid projectId,
        HttpContext httpContext,
        ProjectService projectService,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();

        if (userId is null)
        {
            return ApiResults.Unauthorized();
        }

        var result = await projectService.GetAsync(projectId, userId.Value, cancellationToken);

        return result.IsSuccess ? Results.Ok(result.Value) : ApiResults.Problem(result.Error!);
    }

    private static async Task<IResult> UpdateAsync(
        Guid projectId,
        UpdateProjectRequest request,
        UpdateProjectRequestValidator validator,
        HttpContext httpContext,
        ProjectService projectService,
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

        var result = await projectService.UpdateAsync(
            projectId,
            userId.Value,
            request,
            cancellationToken);

        return result.IsSuccess ? Results.Ok(result.Value) : ApiResults.Problem(result.Error!);
    }

    private static async Task<IResult> DeleteAsync(
        Guid projectId,
        HttpContext httpContext,
        ProjectService projectService,
        bool? purge,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();

        if (userId is null)
        {
            return ApiResults.Unauthorized();
        }

        var result = await projectService.DeleteAsync(
            projectId,
            userId.Value,
            purge ?? false,
            cancellationToken);

        return result.IsSuccess ? Results.NoContent() : ApiResults.Problem(result.Error!);
    }

    private static async Task<IResult> GetStatsAsync(
        Guid projectId,
        HttpContext httpContext,
        ProjectService projectService,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();

        if (userId is null)
        {
            return ApiResults.Unauthorized();
        }

        var result = await projectService.GetStatsAsync(projectId, userId.Value, cancellationToken);

        return result.IsSuccess ? Results.Ok(result.Value) : ApiResults.Problem(result.Error!);
    }
}
