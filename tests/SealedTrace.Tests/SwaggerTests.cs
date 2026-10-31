using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace Arovyx.SealedTrace.Tests;

[Collection(ApiTestGroup.Name)]
public sealed class SwaggerTests(SealedTraceApiFactory factory)
{
    [Fact]
    public async Task Swagger_document_exposes_security_schemes()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("info").GetProperty("title").GetString()
            .Should().Be("SealedTrace API");

        var securitySchemes = document.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes");

        securitySchemes.TryGetProperty("Bearer", out _).Should().BeTrue();
        securitySchemes.TryGetProperty("ApiKey", out _).Should().BeTrue();
    }
}
