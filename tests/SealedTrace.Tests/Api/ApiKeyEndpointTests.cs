using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Arovyx.SealedTrace.Tests.Api;

[Collection(ApiTestGroup.Name)]
public sealed class ApiKeyEndpointTests(SealedTraceApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static string ReadBody(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync().Result;

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private async Task<(HttpClient Client, Guid ProjectId)> AuthenticatedWithProjectAsync()
    {
        var client = factory.CreateClient();
        var email = UniqueEmail("keyuser");

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "correct horse battery staple",
            name = "Key User",
        });

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "correct horse battery staple",
        });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginBody = JsonDocument.Parse(ReadBody(loginResponse));
        var accessToken = loginBody.RootElement.GetProperty("accessToken").GetString()!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var projectResponse = await client.PostAsJsonAsync("/api/v1/projects", new
        {
            name = $"Key Test Project {Guid.NewGuid():N}",
            environment = "prod",
        });
        projectResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var projectBody = JsonDocument.Parse(ReadBody(projectResponse));
        var projectId = projectBody.RootElement.GetProperty("project").GetProperty("id").GetGuid();

        return (client, projectId);
    }

    [Fact]
    public async Task List_api_keys_returns_prefixes_not_plaintext()
    {
        var (client, projectId) = await AuthenticatedWithProjectAsync();

        using var response = await client.GetAsync($"/api/v1/projects/{projectId}/api-keys");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(ReadBody(response));
        var keys = body.RootElement.EnumerateArray().ToList();
        keys.Should().HaveCount(1);

        keys[0].GetProperty("prefix").GetString().Should().NotBeNullOrWhiteSpace().And.HaveLength(8);
        keys[0].GetProperty("isRevoked").GetBoolean().Should().BeFalse();
        keys[0].TryGetProperty("plaintext", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Create_api_key_returns_plaintext_once()
    {
        var (client, projectId) = await AuthenticatedWithProjectAsync();

        using var response = await client.PostAsync($"/api/v1/projects/{projectId}/api-keys", null);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = JsonDocument.Parse(ReadBody(response));
        var plaintext = body.RootElement.GetProperty("plaintext").GetString()!;
        plaintext.Should().NotBeNullOrWhiteSpace().And.HaveLength(43);
        var prefix = body.RootElement.GetProperty("prefix").GetString()!;
        prefix.Should().Be(plaintext[..8]);
    }

    [Fact]
    public async Task Create_sixth_active_key_returns_key_limit_reached()
    {
        var (client, projectId) = await AuthenticatedWithProjectAsync();

        // First key already exists from project creation. Create 4 more (total 5 active).
        for (var i = 0; i < 4; i++)
        {
            var resp = await client.PostAsync($"/api/v1/projects/{projectId}/api-keys", null);
            resp.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // 6th active key should be rejected.
        using var response = await client.PostAsync($"/api/v1/projects/{projectId}/api-keys", null);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = JsonDocument.Parse(ReadBody(response));
        body.RootElement.GetProperty("code").GetString().Should().Be("key_limit_reached");
    }

    [Fact]
    public async Task Revoke_api_key_marks_it_revoked()
    {
        var (client, projectId) = await AuthenticatedWithProjectAsync();

        var createResponse = await client.PostAsync($"/api/v1/projects/{projectId}/api-keys", null);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createBody = JsonDocument.Parse(ReadBody(createResponse));
        var keyId = createBody.RootElement.GetProperty("id").GetGuid();

        using var revokeResponse = await client.DeleteAsync($"/api/v1/projects/{projectId}/api-keys/{keyId}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var listResponse = await client.GetAsync($"/api/v1/projects/{projectId}/api-keys");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listBody = JsonDocument.Parse(ReadBody(listResponse));
        var keys = listBody.RootElement.EnumerateArray().ToList();
        var revokedKey = keys.FirstOrDefault(k => k.GetProperty("id").GetGuid() == keyId);
        revokedKey.Should().NotBeNull();
        revokedKey.GetProperty("isRevoked").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Access_api_keys_for_other_project_returns_404()
    {
        var (clientA, projectIdA) = await AuthenticatedWithProjectAsync();
        var (clientB, _) = await AuthenticatedWithProjectAsync();

        using var response = await clientB.GetAsync($"/api/v1/projects/{projectIdA}/api-keys");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
