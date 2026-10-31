using System.Security.Claims;
using Arovyx.SealedTrace.Api.Validation;
using Arovyx.SealedTrace.Core.Dtos.Stats;
using Arovyx.SealedTrace.Core.Interfaces;
using Arovyx.SealedTrace.Infrastructure.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arovyx.SealedTrace.Api.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/stats")]
[Authorize]
public sealed class StatsController : ControllerBase
{
    private readonly IStatsService _statsService;
    private readonly IValidator<StatsQueryRequest> _statsValidator;

    public StatsController(
        IStatsService statsService,
        IValidator<StatsQueryRequest> statsValidator)
    {
        _statsService = statsService;
        _statsValidator = statsValidator;
    }

    [HttpGet]
    public async Task<IActionResult> GetStats(
        Guid projectId,
        [FromQuery] StatsQueryRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await _statsValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ValidationProblem(validationResult);
        }

        var ownerId = GetCurrentUserId();

        try
        {
            var response = await _statsService.GetStatsAsync(projectId, ownerId, request, cancellationToken);
            return Ok(response);
        }
        catch (ProjectNotFoundException)
        {
            return NotFound(Problem("not_found", "Project not found.", System.Net.HttpStatusCode.NotFound));
        }
        catch (InvalidRangeException ex)
        {
            return BadRequest(Problem("invalid_range", ex.Message, System.Net.HttpStatusCode.BadRequest));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.GetType().Name, message = ex.Message, inner = ex.InnerException?.Message });
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

    private static object Problem(string code, string title, System.Net.HttpStatusCode status)
    {
        return Problem(code, title, status, null);
    }
}
