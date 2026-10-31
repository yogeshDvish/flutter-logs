using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace Arovyx.SealedTrace.Tests;

[Collection(ApiTestGroup.Name)]
public sealed class HealthEndpointTests(SealedTraceApiFactory factory)
{
    [Fact]
    public async Task Liveness_returns_healthy()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task Readiness_checks_database()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("status").GetString().Should().Be("Healthy");

        document.RootElement.GetProperty("checks")
            .TryGetProperty("database", out var database)
            .Should()
            .BeTrue();

        database.GetString().Should().Be("Healthy");
    }
}
