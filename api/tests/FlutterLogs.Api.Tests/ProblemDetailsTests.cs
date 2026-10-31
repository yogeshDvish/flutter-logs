using System.Net;
using Shouldly;

namespace FlutterLogs.Api.Tests;

[Collection(ApiTestGroup.Name)]
public sealed class ProblemDetailsTests(ApiFactory factory)
{
    [Fact]
    public async Task Unknown_route_returns_problem_details_with_machine_readable_code()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/does-not-exist");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        response.Content.Headers.ContentType?.MediaType
            .ShouldBe("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("\"status\":404");
        body.ShouldContain("\"code\":\"not_found\"");
        body.ShouldContain("\"instance\":\"/api/v1/does-not-exist\"");
    }

    [Fact]
    public async Task Correlation_id_from_the_request_is_echoed_and_traced()
    {
        const string correlationId = "test-correlation-1";

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/does-not-exist");
        request.Headers.Add("X-Correlation-Id", correlationId);

        using var response = await client.SendAsync(request);

        response.Headers.TryGetValues("X-Correlation-Id", out var headerValues)
            .ShouldBeTrue();
        headerValues.ShouldNotBeNull();
        headerValues.ShouldContain(correlationId);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain(correlationId);
    }

    [Fact]
    public async Task Correlation_id_is_generated_when_absent_or_invalid()
    {
        using var client = factory.CreateClient();

        using (var response = await client.GetAsync("/api/v1/does-not-exist"))
        {
            response.Headers.TryGetValues("X-Correlation-Id", out var values)
                .ShouldBeTrue();
            values.ShouldNotBeNull();
            values.Single().ShouldNotBeNullOrWhiteSpace();
        }

        // Spaces and symbols outside the allow-list must be replaced, not echoed.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/does-not-exist");
        request.Headers.Add("X-Correlation-Id", "not allowed value#1");

        using var rejected = await client.SendAsync(request);
        rejected.Headers.TryGetValues("X-Correlation-Id", out var generated)
            .ShouldBeTrue();
        generated.ShouldNotBeNull();
        generated.Single().ShouldNotBe("not allowed value#1");
    }
}
