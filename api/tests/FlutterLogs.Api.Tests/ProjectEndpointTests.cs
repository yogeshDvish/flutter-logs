using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FlutterLogs.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FlutterLogs.Api.Tests;

[Collection(ApiTestGroup.Name)]
public sealed class ProjectEndpointTests(ApiFactory factory)
{
    private const string StrongPassword = "CorrectHorseBattery9!";

    [Fact]
    public async Task Create_project_issues_a_plaintext_key_exactly_once()
    {
        using var client = CreateClient();
        var session = await RegisterAndLoginAsync(client);

        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/projects",
            session.AccessToken,
            new { name = "Checkout App", description = "Production checkout logs" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CreatedProjectPayload>();
        created.ShouldNotBeNull();
        created.Project.Name.ShouldBe("Checkout App");
        created.Project.Description.ShouldBe("Production checkout logs");
        created.Project.IngestEnabled.ShouldBeTrue();
        created.Project.ArchivedAt.ShouldBeNull();
        created.Project.ActiveKeyCount.ShouldBe(1);
        created.Project.Stats.Last24Hours.Total.ShouldBe(0);

        var parts = created.Key.Plaintext.Split('_', 4);
        parts.Length.ShouldBe(4);
        parts[0].ShouldBe("flk");
        parts[1].ShouldBe("live");
        parts[2].ShouldBe(created.Key.Prefix);
        parts[2].Length.ShouldBe(8);
        parts[3].Length.ShouldBe(43);

        var list = await GetJsonAsync<ProjectPayload[]>(
            client,
            "/api/v1/projects",
            session.AccessToken);
        list.ShouldNotBeNull();
        list.Length.ShouldBe(1);
        list[0].Id.ShouldBe(created.Project.Id);

        var listRaw = await (await SendAsync(
            client,
            HttpMethod.Get,
            "/api/v1/projects",
            session.AccessToken)).Content.ReadAsStringAsync();
        listRaw.ShouldNotContain(created.Key.Plaintext, Case.Sensitive);
        listRaw.ShouldNotContain("plaintext", Case.Insensitive);

        var keys = await GetJsonAsync<KeyPayload[]>(
            client,
            $"/api/v1/projects/{created.Project.Id}/keys",
            session.AccessToken);
        keys.ShouldNotBeNull();
        keys.Length.ShouldBe(1);
        keys[0].Prefix.ShouldBe(created.Key.Prefix);
        keys[0].Status.ShouldBe("active");

        var keysRaw = await (await SendAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/projects/{created.Project.Id}/keys",
            session.AccessToken)).Content.ReadAsStringAsync();
        keysRaw.ShouldNotContain(created.Key.Plaintext, Case.Sensitive);
        keysRaw.ShouldNotContain("keyHash", Case.Insensitive);
        keysRaw.ShouldNotContain("plaintext", Case.Insensitive);
    }

    [Fact]
    public async Task Projects_are_owner_scoped_and_cross_tenant_access_returns_404()
    {
        using var ownerClient = CreateClient();
        var owner = await RegisterAndLoginAsync(ownerClient);
        var project = await CreateProjectAsync(ownerClient, owner.AccessToken, "Owner project");

        using var otherClient = CreateClient();
        var other = await RegisterAndLoginAsync(otherClient);

        var otherList = await GetJsonAsync<ProjectPayload[]>(
            otherClient,
            "/api/v1/projects",
            other.AccessToken);
        otherList.ShouldNotBeNull();
        otherList.ShouldBeEmpty();

        var paths = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Get, $"/api/v1/projects/{project.Id}"),
            (HttpMethod.Get, $"/api/v1/projects/{project.Id}/keys"),
            (HttpMethod.Get, $"/api/v1/projects/{project.Id}/stats"),
        };

        foreach (var (method, path) in paths)
        {
            var response = await SendAsync(otherClient, method, path, other.AccessToken);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ReadProblemAsync(response)).Code.ShouldBe("not_found");
        }

        var patch = await SendJsonAsync(
            otherClient,
            HttpMethod.Patch,
            $"/api/v1/projects/{project.Id}",
            other.AccessToken,
            new { name = "Hijacked" });
        patch.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var createKey = await SendJsonAsync(
            otherClient,
            HttpMethod.Post,
            $"/api/v1/projects/{project.Id}/keys",
            other.AccessToken,
            new { label = "hijack" });
        createKey.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var delete = await SendAsync(
            otherClient,
            HttpMethod.Delete,
            $"/api/v1/projects/{project.Id}",
            other.AccessToken);
        delete.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var ownerDetail = await GetJsonAsync<ProjectPayload>(
            ownerClient,
            $"/api/v1/projects/{project.Id}",
            owner.AccessToken);
        ownerDetail.ShouldNotBeNull();
        ownerDetail.Name.ShouldBe("Owner project");
    }

    [Fact]
    public async Task Key_rotation_keeps_a_grace_window_and_enforces_the_two_key_limit()
    {
        using var client = CreateClient();
        var session = await RegisterAndLoginAsync(client);
        var project = await CreateProjectAsync(client, session.AccessToken, "Rotation project");

        var rotated = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/projects/{project.Id}/keys",
            session.AccessToken,
            new { label = "Rotated", graceHours = 1 });
        rotated.StatusCode.ShouldBe(HttpStatusCode.Created);

        var rotatedKey = await rotated.Content.ReadFromJsonAsync<IssuedKeyPayload>();
        rotatedKey.ShouldNotBeNull();
        rotatedKey.Prefix.ShouldNotBe(project.KeyPrefix);
        rotatedKey.Plaintext.ShouldNotBe(project.KeyPlaintext);
        rotatedKey.Label.ShouldBe("Rotated");

        var keys = await GetJsonAsync<KeyPayload[]>(
            client,
            $"/api/v1/projects/{project.Id}/keys",
            session.AccessToken);
        keys.ShouldNotBeNull();
        keys.Length.ShouldBe(2);
        keys.Single(key => key.Prefix == project.KeyPrefix).Status.ShouldBe("grace");
        keys.Single(key => key.Prefix == rotatedKey.Prefix).Status.ShouldBe("active");

        var third = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/projects/{project.Id}/keys",
            session.AccessToken,
            new { label = "Too many" });
        third.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblemAsync(third)).Code.ShouldBe("key_limit_reached");

        var oldKey = keys.Single(key => key.Prefix == project.KeyPrefix);
        var revoke = await SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/projects/{project.Id}/keys/{oldKey.Id}",
            session.AccessToken);
        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var afterRevoke = await GetJsonAsync<KeyPayload[]>(
            client,
            $"/api/v1/projects/{project.Id}/keys",
            session.AccessToken);
        afterRevoke.ShouldNotBeNull();
        afterRevoke.Single(key => key.Id == oldKey.Id).Status.ShouldBe("revoked");

        var replacement = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/projects/{project.Id}/keys",
            session.AccessToken,
            new { label = "After revoke" });
        replacement.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Rotation_with_zero_grace_revokes_the_previous_key_immediately()
    {
        using var client = CreateClient();
        var session = await RegisterAndLoginAsync(client);
        var project = await CreateProjectAsync(client, session.AccessToken, "Immediate rotation");

        var rotated = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/projects/{project.Id}/keys",
            session.AccessToken,
            new { graceHours = 0 });
        rotated.StatusCode.ShouldBe(HttpStatusCode.Created);

        var keys = await GetJsonAsync<KeyPayload[]>(
            client,
            $"/api/v1/projects/{project.Id}/keys",
            session.AccessToken);
        keys.ShouldNotBeNull();
        keys.Single(key => key.Prefix == project.KeyPrefix).Status.ShouldBe("revoked");
    }

    [Fact]
    public async Task Archive_hides_the_project_and_blocks_new_keys_until_purged()
    {
        using var client = CreateClient();
        var session = await RegisterAndLoginAsync(client);
        var project = await CreateProjectAsync(client, session.AccessToken, "Archive project");

        var archive = await SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/projects/{project.Id}",
            session.AccessToken);
        archive.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var visible = await GetJsonAsync<ProjectPayload[]>(
            client,
            "/api/v1/projects",
            session.AccessToken);
        visible.ShouldNotBeNull();
        visible.ShouldBeEmpty();

        var withArchived = await GetJsonAsync<ProjectPayload[]>(
            client,
            "/api/v1/projects?includeArchived=true",
            session.AccessToken);
        withArchived.ShouldNotBeNull();
        withArchived.Length.ShouldBe(1);
        withArchived[0].ArchivedAt.ShouldNotBeNull();
        withArchived[0].IngestEnabled.ShouldBeFalse();

        var detail = await GetJsonAsync<ProjectPayload>(
            client,
            $"/api/v1/projects/{project.Id}",
            session.AccessToken);
        detail.ShouldNotBeNull();
        detail.ArchivedAt.ShouldNotBeNull();

        var key = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/projects/{project.Id}/keys",
            session.AccessToken,
            new { label = "blocked" });
        key.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblemAsync(key)).Code.ShouldBe("project_archived");

        var patch = await SendJsonAsync(
            client,
            HttpMethod.Patch,
            $"/api/v1/projects/{project.Id}",
            session.AccessToken,
            new { name = "Renamed" });
        patch.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var purge = await SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/projects/{project.Id}?purge=true",
            session.AccessToken);
        purge.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var gone = await SendAsync(
            client,
            HttpMethod.Get,
            $"/api/v1/projects/{project.Id}",
            session.AccessToken);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_changes_fields_and_validates_input()
    {
        using var client = CreateClient();
        var session = await RegisterAndLoginAsync(client);
        var project = await CreateProjectAsync(client, session.AccessToken, "Settings project");

        var update = await SendJsonAsync(
            client,
            HttpMethod.Patch,
            $"/api/v1/projects/{project.Id}",
            session.AccessToken,
            new { name = "Renamed project", description = "Updated", ingestEnabled = false });
        update.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await update.Content.ReadFromJsonAsync<ProjectPayload>();
        updated.ShouldNotBeNull();
        updated.Name.ShouldBe("Renamed project");
        updated.Description.ShouldBe("Updated");
        updated.IngestEnabled.ShouldBeFalse();

        var empty = await SendJsonAsync(
            client,
            HttpMethod.Patch,
            $"/api/v1/projects/{project.Id}",
            session.AccessToken,
            new { });
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(empty)).Code.ShouldBe("validation_failed");

        var invalidCreate = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/projects",
            session.AccessToken,
            new { name = "" });
        invalidCreate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Project_creation_is_capped_per_account()
    {
        using var client = CreateClient();
        var session = await RegisterAndLoginAsync(client);

        for (var index = 0; index < 20; index++)
        {
            await CreateProjectAsync(client, session.AccessToken, $"Project {index}");
        }

        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/projects",
            session.AccessToken,
            new { name = "One too many" });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblemAsync(response)).Code.ShouldBe("project_limit_reached");
    }

    [Fact]
    public async Task Audit_trail_records_project_and_key_actions_without_secrets()
    {
        using var client = CreateClient();
        var session = await RegisterAndLoginAsync(client);
        var project = await CreateProjectAsync(client, session.AccessToken, "Audited project");

        var rotated = await SendJsonAsync(
            client,
            HttpMethod.Post,
            $"/api/v1/projects/{project.Id}/keys",
            session.AccessToken,
            new { label = "Audited rotation" });
        rotated.StatusCode.ShouldBe(HttpStatusCode.Created);
        var rotatedKey = await rotated.Content.ReadFromJsonAsync<IssuedKeyPayload>();
        rotatedKey.ShouldNotBeNull();

        var keys = await GetJsonAsync<KeyPayload[]>(
            client,
            $"/api/v1/projects/{project.Id}/keys",
            session.AccessToken);
        keys.ShouldNotBeNull();
        var oldKey = keys.Single(key => key.Prefix == project.KeyPrefix);

        var revoke = await SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/projects/{project.Id}/keys/{oldKey.Id}",
            session.AccessToken);
        revoke.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var archive = await SendAsync(
            client,
            HttpMethod.Delete,
            $"/api/v1/projects/{project.Id}",
            session.AccessToken);
        archive.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var audits = await db.AuditEvents
            .AsNoTracking()
            .Where(audit => audit.ProjectId == project.Id)
            .ToListAsync();

        var actions = audits.Select(audit => audit.Action).ToArray();
        actions.ShouldContain("project.create");
        actions.ShouldContain("key.create");
        actions.ShouldContain("key.rotate");
        actions.ShouldContain("key.revoke");
        actions.ShouldContain("project.archive");

        audits.ShouldAllBe(audit =>
            audit.Metadata == null
            || (!audit.Metadata.Contains(project.KeyPlaintext, StringComparison.Ordinal)
                && !audit.Metadata.Contains(rotatedKey.Plaintext, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/projects");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblemAsync(response)).Code.ShouldBe("unauthorized");
    }

    private HttpClient CreateClient() => factory.CreateClient();

    private static async Task<(HttpResponseMessage Response, CreatedProjectPayload Project, string KeyPrefix, string KeyPlaintext)>
        CreateProjectWithResponseAsync(HttpClient client, string accessToken, string name)
    {
        var response = await SendJsonAsync(
            client,
            HttpMethod.Post,
            "/api/v1/projects",
            accessToken,
            new { name });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<CreatedProjectPayload>();
        created.ShouldNotBeNull();

        return (response, created, created.Key.Prefix, created.Key.Plaintext);
    }

    private static async Task<ProjectFixture> CreateProjectAsync(
        HttpClient client,
        string accessToken,
        string name)
    {
        var (_, created, keyPrefix, keyPlaintext) =
            await CreateProjectWithResponseAsync(client, accessToken, name);

        return new ProjectFixture(created.Project.Id, keyPrefix, keyPlaintext);
    }

    private static async Task<Session> RegisterAndLoginAsync(HttpClient client)
    {
        var email = $"projects-{Guid.NewGuid():N}@example.com";

        var register = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password = StrongPassword });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);

        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = StrongPassword });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);

        var session = await login.Content.ReadFromJsonAsync<AuthPayload>();
        session.ShouldNotBeNull();

        return new Session(session.AccessToken);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string accessToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string accessToken,
        object body)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request);
    }

    private static async Task<T?> GetJsonAsync<T>(
        HttpClient client,
        string path,
        string accessToken)
    {
        var response = await SendAsync(client, HttpMethod.Get, path, accessToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<T>();
    }

    private static async Task<ProblemPayload> ReadProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>();
        problem.ShouldNotBeNull();
        return problem;
    }

    private sealed record Session(string AccessToken);

    private sealed record ProjectFixture(Guid Id, string KeyPrefix, string KeyPlaintext);

    private sealed record UserPayload(Guid Id, string Email);

    private sealed record AuthPayload(string AccessToken, UserPayload User);

    private sealed record StatsBucketPayload(int Total, int Errors);

    private sealed record StatsPayload(
        StatsBucketPayload Last24Hours,
        StatsBucketPayload Last7Days,
        DateTime? LastLogAt);

    private sealed record ProjectPayload(
        Guid Id,
        string Name,
        string? Description,
        bool IngestEnabled,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? ArchivedAt,
        int ActiveKeyCount,
        StatsPayload Stats);

    private sealed record IssuedKeyPayload(
        Guid Id,
        string Prefix,
        string? Label,
        DateTime CreatedAt,
        DateTime? GraceUntil,
        string Plaintext);

    private sealed record CreatedProjectPayload(ProjectPayload Project, IssuedKeyPayload Key);

    private sealed record KeyPayload(
        Guid Id,
        string Prefix,
        string? Label,
        string Status,
        DateTime CreatedAt,
        DateTime? LastUsedAt,
        DateTime? GraceUntil,
        DateTime? RevokedAt);

    private sealed record ProblemPayload(string Title, int Status, string Code);
}
