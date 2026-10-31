using System.Net;
using Arovyx.SealedTrace.Api.Security;
using Arovyx.SealedTrace.Core.Dtos.Ingest;
using Arovyx.SealedTrace.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Arovyx.SealedTrace.Api.Controllers;

[ApiController]
[Route("api/v1/ingest")]
public sealed class IngestController : ControllerBase
{
    private readonly IIngestService _ingestService;

    public IngestController(IIngestService ingestService)
    {
        _ingestService = ingestService;
    }

    [HttpPost("logs")]
    public async Task<IActionResult> Ingest(
        [FromBody] IngestRequest request,
        CancellationToken cancellationToken)
    {
        var projectId = ApiKeyAuthenticationHandler.GetProjectId(HttpContext);

        var response = await _ingestService.IngestAsync(projectId, request, cancellationToken);

        return Accepted(response);
    }

    private static object Problem(string code, string title, HttpStatusCode status, string detail)
    {
        return new
        {
            type = $"https://sealedtrace.dev/errors/{code}",
            title,
            status = (int)status,
            detail,
            code,
            traceId = System.Diagnostics.Activity.Current?.Id ?? string.Empty,
        };
    }
}
