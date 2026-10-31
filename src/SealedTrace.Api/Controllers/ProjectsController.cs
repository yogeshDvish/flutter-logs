using System.Security.Claims;
using Arovyx.SealedTrace.Api.Validation;
using Arovyx.SealedTrace.Core.Dtos.ApiKeys;
using Arovyx.SealedTrace.Core.Dtos.Projects;
using Arovyx.SealedTrace.Core.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arovyx.SealedTrace.Api.Controllers;

[ApiController]
[Route("api/v1/projects")]
[Authorize]
public sealed class ProjectsController : ControllerBase
{
    private readonly IProjectService _projectService;
    private readonly IValidator<CreateProjectRequest> _createValidator;
    private readonly IValidator<UpdateProjectRequest> _updateValidator;

    public ProjectsController(
        IProjectService projectService,
        IValidator<CreateProjectRequest> createValidator,
        IValidator<UpdateProjectRequest> updateValidator)
    {
        _projectService = projectService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var ownerId = GetCurrentUserId();
        var projects = await _projectService.ListProjectsAsync(ownerId, cancellationToken);
        return Ok(projects);
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(validationResult);
        }

        var ownerId = GetCurrentUserId();
        var (created, apiKeyPlaintext) = await _projectService.CreateProjectAsync(ownerId, request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = created.Project.Id }, new
        {
            project = created.Project,
            apiKey = new
            {
                id = created.ApiKey.Id,
                prefix = created.ApiKey.Prefix,
                plaintext = apiKeyPlaintext,
                createdAtUtc = created.ApiKey.CreatedAtUtc,
            },
            publicKeyPem = created.PublicKeyPem,
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var ownerId = GetCurrentUserId();
        var project = await _projectService.GetProjectAsync(id, ownerId, cancellationToken);

        if (project is null)
        {
            return NotFound(Problem("not_found", "Project not found.", System.Net.HttpStatusCode.NotFound));
        }

        return Ok(project);
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateProjectRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(validationResult);
        }

        var ownerId = GetCurrentUserId();

        try
        {
            var project = await _projectService.UpdateProjectAsync(id, ownerId, request, cancellationToken);
            return Ok(project);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(Problem("not_found", "Project not found.", System.Net.HttpStatusCode.NotFound));
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var ownerId = GetCurrentUserId();

        try
        {
            await _projectService.DeleteProjectAsync(id, ownerId, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound(Problem("not_found", "Project not found.", System.Net.HttpStatusCode.NotFound));
        }
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User ID claim not found.");
        return Guid.Parse(claim);
    }

    private BadRequestObjectResult ValidationProblem(FluentValidation.Results.ValidationResult result)
    {
        var errors = result.Errors.Select(e => new { field = e.PropertyName, message = e.ErrorMessage });
        return BadRequest(Problem("validation_failed", "One or more fields are invalid.", System.Net.HttpStatusCode.BadRequest, errors));
    }

    private static object Problem(string code, string title, System.Net.HttpStatusCode status, object? errors = null)
    {
        return new
        {
            type = $"https://sealedtrace.dev/errors/{code}",
            title,
            status = (int)status,
            detail = title,
            code,
            traceId = System.Diagnostics.Activity.Current?.Id ?? string.Empty,
            errors,
        };
    }
}
