using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Arovyx.SealedTrace.Tests.Api;

[Collection(ApiTestGroup.Name)]
public sealed class ProjectEndpointTests(SealedTraceApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static string ReadBody(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync().Result;

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private async Task<(HttpClient Client, string AccessToken)> AuthenticatedClientAsync()
    {
        var client = factory.CreateClient();
        var email = UniqueEmail("projuser");

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "correct horse battery staple",
            name = "Proj User",
        });

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "correct horse battery staple",
        });

        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(ReadBody(loginResponse));
        var accessToken = body.RootElement.GetProperty("accessToken").GetString()!;

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        return (client, accessToken);
    }

    [Fact]
    public async Task Create_project_returns_201_with_api_key_and_public_key()
    {
        var (client, _) = await AuthenticatedClientAsync();
        var name = $"My Production App {Guid.NewGuid():N}";

        using var response = await client.PostAsJsonAsync("/api/v1/projects", new
        {
            name,
            description = "Production mobile client",
            environment = "prod",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = JsonDocument.Parse(ReadBody(response));
        var project = body.RootElement.GetProperty("project");
        project.GetProperty("name").GetString().Should().Be(name);
        project.GetProperty("slug").GetString().Should().StartWith("my-production-app-");
        project.GetProperty("environment").GetString().Should().Be("prod");
        project.GetProperty("description").GetString().Should().Be("Production mobile client");
        project.GetProperty("activeApiKeyCount").GetInt32().Should().Be(1);

        var apiKey = body.RootElement.GetProperty("apiKey");
        apiKey.GetProperty("prefix").GetString().Should().NotBeNullOrWhiteSpace().And.HaveLength(8);
        apiKey.GetProperty("plaintext").GetString().Should().NotBeNullOrWhiteSpace().And.HaveLength(43);

        var publicKeyPem = body.RootElement.GetProperty("publicKeyPem").GetString()!;
        publicKeyPem.Should().StartWith("-----BEGIN PUBLIC KEY-----");
        publicKeyPem.Should().EndWith("-----END PUBLIC KEY-----");
    }

    [Fact]
    public async Task Create_project_generates_unique_slug_on_duplicate_name()
    {
        var (client, _) = await AuthenticatedClientAsync();
        var name = $"Duplicate App {Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/api/v1/projects", new
        {
            name,
            environment = "prod",
        });

        using var response2 = await client.PostAsJsonAsync("/api/v1/projects", new
        {
            name,
            environment = "prod",
        });

        response2.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = JsonDocument.Parse(ReadBody(response2));
        var slug = body.RootElement.GetProperty("project").GetProperty("slug").GetString();
        slug.Should().StartWith($"{GenerateExpectedSlug(name)}-2");
    }

    private static string GenerateExpectedSlug(string name)
    {
        var slug = name.ToLowerInvariant();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9]+", "-");
        slug = slug.Trim('-');
        return slug;
    }

    [Fact]
    public async Task List_projects_returns_only_owned_projects()
    {
        var (clientA, _) = await AuthenticatedClientAsync();
        var (clientB, _) = await AuthenticatedClientAsync();
        var uniqueName = $"Owner A Project {Guid.NewGuid():N}";

        await clientA.PostAsJsonAsync("/api/v1/projects", new
        {
            name = uniqueName,
            environment = "prod",
        });

        using var response = await clientB.GetAsync("/api/v1/projects");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(ReadBody(response));
        var projects = body.RootElement.EnumerateArray().ToList();
        projects.Should().NotContain(p => p.GetProperty("name").GetString() == uniqueName);
    }

    [Fact]
    public async Task Get_project_returns_404_for_non_owner()
    {
        var (clientA, _) = await AuthenticatedClientAsync();
        var (clientB, _) = await AuthenticatedClientAsync();

        var createResponse = await clientA.PostAsJsonAsync("/api/v1/projects", new
        {
            name = $"Secret Project {Guid.NewGuid():N}",
            environment = "prod",
        });

        var createBody = JsonDocument.Parse(ReadBody(createResponse));
        var projectId = createBody.RootElement.GetProperty("project").GetProperty("id").GetGuid();

        using var response = await clientB.GetAsync($"/api/v1/projects/{projectId}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_project_returns_200_created_project()
    {
        var (client, _) = await AuthenticatedClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/v1/projects", new
        {
            name = $"Get Test Project {Guid.NewGuid():N}",
            environment = "prod",
        });

        var createBody = JsonDocument.Parse(ReadBody(createResponse));
        var projectId = createBody.RootElement.GetProperty("project").GetProperty("id").GetGuid();
        var projectName = createBody.RootElement.GetProperty("project").GetProperty("name").GetString();

        using var response = await client.GetAsync($"/api/v1/projects/{projectId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(ReadBody(response));
        body.RootElement.GetProperty("id").GetGuid().Should().Be(projectId);
        body.RootElement.GetProperty("name").GetString().Should().Be(projectName);
    }

    [Fact]
    public async Task Update_project_changes_name_and_environment()
    {
        var (client, _) = await AuthenticatedClientAsync();
        var originalName = $"Old Name {Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync("/api/v1/projects", new
        {
            name = originalName,
            environment = "dev",
        });

        var createBody = JsonDocument.Parse(ReadBody(createResponse));
        var projectId = createBody.RootElement.GetProperty("project").GetProperty("id").GetGuid();
        var originalSlug = createBody.RootElement.GetProperty("project").GetProperty("slug").GetString();

        using var response = await client.PatchAsJsonAsync($"/api/v1/projects/{projectId}", new
        {
            name = "New Name",
            environment = "prod",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(ReadBody(response));
        body.RootElement.GetProperty("name").GetString().Should().Be("New Name");
        body.RootElement.GetProperty("environment").GetString().Should().Be("prod");
        body.RootElement.GetProperty("slug").GetString().Should().Be(originalSlug); // slug immutable
    }

    [Fact]
    public async Task Delete_project_returns_204()
    {
        var (client, _) = await AuthenticatedClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/v1/projects", new
        {
            name = $"To Be Deleted {Guid.NewGuid():N}",
            environment = "prod",
        });

        var createBody = JsonDocument.Parse(ReadBody(createResponse));
        var projectId = createBody.RootElement.GetProperty("project").GetProperty("id").GetGuid();

        using var response = await client.DeleteAsync($"/api/v1/projects/{projectId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var getResponse = await client.GetAsync($"/api/v1/projects/{projectId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
