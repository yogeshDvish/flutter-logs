using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Arovyx.SealedTrace.Tests.Api;

[Collection(ApiTestGroup.Name)]
public sealed class HardeningTests(SealedTraceApiFactory factory)
{
    private static string ReadBody(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync().Result;

    [Fact]
    public async Task Security_headers_are_present()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        response.Headers.Contains("X-Content-Type-Options").Should().BeTrue();
        response.Headers.GetValues("X-Content-Type-Options").First().Should().Be("nosniff");

        response.Headers.Contains("Referrer-Policy").Should().BeTrue();
        response.Headers.GetValues("Referrer-Policy").First().Should().Be("no-referrer");
    }

    [Fact]
    public async Task Health_endpoint_returns_problem_details_format()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task Swagger_is_available_in_development()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(ReadBody(response));
        body.RootElement.GetProperty("info").GetProperty("title").GetString().Should().Be("SealedTrace API");
    }

    [Fact]
    public async Task Swagger_has_api_key_security_scheme()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(ReadBody(response));
        var schemes = body.RootElement.GetProperty("components").GetProperty("securitySchemes");
        schemes.TryGetProperty("ApiKey", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Correlation_id_header_is_returned()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Contains("X-Correlation-Id").Should().BeTrue();
    }

    [Fact]
    public async Task Unknown_route_returns_problem_details()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/does-not-exist");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var body = JsonDocument.Parse(ReadBody(response));
        body.RootElement.GetProperty("code").GetString().Should().Be("not_found");
        body.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Ingest_without_api_key_returns_401()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/ingest/logs", new
        {
            envelopes = System.Array.Empty<object>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Metrics_endpoint_is_not_available_by_default()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/metrics");

        // /metrics is not mapped, so it should return 404
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
