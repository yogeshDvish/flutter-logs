using System.Security.Claims;
using Arovyx.SealedTrace.Core.Dtos.ApiKeys;
using Arovyx.SealedTrace.Core.Interfaces;
using Arovyx.SealedTrace.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Arovyx.SealedTrace.Api.Controllers;

[ApiController]
[Route("api/v1/projects/{projectId:guid}/api-keys")]
[Authorize]
public sealed class ApiKeysController : ControllerBase
{
    private readonly IApiKeyService _apiKeyService;

    public ApiKeysController(IApiKeyService apiKeyService)
    {
        _apiKeyService = apiKeyService;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid projectId, CancellationToken cancellationToken)
    {
        var ownerId = GetCurrentUserId();

        try
        {
            var keys = await _apiKeyService.ListApiKeysAsync(projectId, ownerId, cancellationToken);
            return Ok(keys);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(Problem("not_found", "Project not found.", System.Net.HttpStatusCode.NotFound));
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var ownerId = GetCurrentUserId();

        try
        {
            var created = await _apiKeyService.CreateApiKeyAsync(projectId, ownerId, cancellationToken);
            return CreatedAtAction(
                nameof(Revoke),
                new { projectId, keyId = created.Id },
                new
                {
                    id = created.Id,
                    prefix = created.Prefix,
                    plaintext = created.Plaintext,
                    createdAtUtc = created.CreatedAtUtc,
                });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(Problem("not_found", "Project not found.", System.Net.HttpStatusCode.NotFound));
        }
        catch (KeyLimitReachedException)
        {
            return Conflict(Problem("key_limit_reached", "Maximum of 5 active API keys per project reached.", System.Net.HttpStatusCode.Conflict));
        }
    }

    [HttpDelete("{keyId:guid}")]
    public async Task<IActionResult> Revoke(
        Guid projectId,
        Guid keyId,
        CancellationToken cancellationToken)
    {
        var ownerId = GetCurrentUserId();

        try
        {
            await _apiKeyService.RevokeApiKeyAsync(projectId, keyId, ownerId, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound(Problem("not_found", "Project or API key not found.", System.Net.HttpStatusCode.NotFound));
        }
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User ID claim not found.");
        return Guid.Parse(claim);
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
