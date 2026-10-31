using System.Net;
using System.Net.Http.Json;
using Shouldly;

namespace FlutterLogs.Api.Tests;

[Collection(ApiTestGroup.Name)]
public sealed class HealthEndpointTests(ApiFactory factory)
{
    [Fact]
    public async Task Health_endpoint_reports_database_connectivity()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<HealthPayload>();

        payload.ShouldNotBeNull();
        payload.Status.ShouldBe("Healthy");
        payload.Db.ShouldBe("ok");
        payload.Version.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Health_endpoint_is_reachable_anonymously()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private sealed record HealthPayload(string Status, string Db, string Version);
}
