using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace Arovyx.SealedTrace.Tests;

[Collection(ApiTestGroup.Name)]
public sealed class ProblemDetailsTests(SealedTraceApiFactory factory)
{
    [Fact]
    public async Task Unknown_route_returns_problem_details()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("code").GetString().Should().Be("not_found");
        document.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();

        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle();
    }
}
