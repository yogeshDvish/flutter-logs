using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Arovyx.SealedTrace.Tests.Api;

[Collection(ApiTestGroup.Name)]
public sealed class QueryEndpointTests(SealedTraceApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static string ReadBody(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync().Result;

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private async Task<(HttpClient Client, Guid ProjectId, string[] LogIds)> SetupWithLogsAsync()
    {
        var client = factory.CreateClient();
        var email = UniqueEmail("query");

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "correct horse battery staple",
            name = "Query User",
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
            name = $"Query Test Project {Guid.NewGuid():N}",
            environment = "prod",
        });
        projectResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var projectBody = JsonDocument.Parse(ReadBody(projectResponse));
        var projectId = projectBody.RootElement.GetProperty("project").GetProperty("id").GetGuid();

        // Create an API key for ingestion.
        var keyResponse = await client.PostAsync($"/api/v1/projects/{projectId}/api-keys", null);
        keyResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var keyBody = JsonDocument.Parse(ReadBody(keyResponse));
        var apiKey = keyBody.RootElement.GetProperty("plaintext").GetString()!;

        // Ingest some logs via the ingest endpoint.
        using var rsa = System.Security.Cryptography.RSA.Create();
        rsa.ImportFromPem(projectBody.RootElement.GetProperty("publicKeyPem").GetString()!);

        var logIds = new List<string>();
        var envelopes = new List<object>();

        for (var i = 0; i < 5; i++)
        {
            var logId = Guid.NewGuid();
            logIds.Add(logId.ToString());

            var logEvent = new
            {
                id = logId,
                seq = i + 1,
                ts = DateTime.UtcNow.AddMinutes(-i).ToString("o"),
                level = i % 2 == 0 ? "info" : "error",
                tag = "test",
                message = $"Log message {i}",
            };

            var eventsJson = System.Text.Json.JsonSerializer.Serialize(new[] { logEvent });
            var eventsBytes = System.Text.Encoding.UTF8.GetBytes(eventsJson);

            var aesKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[eventsBytes.Length];
            var tag = new byte[16];

            using var aesGcm = new System.Security.Cryptography.AesGcm(aesKey, 16);
            aesGcm.Encrypt(nonce, eventsBytes, ciphertext, tag);

            var wrappedKey = rsa.Encrypt(aesKey, System.Security.Cryptography.RSAEncryptionPadding.OaepSHA256);

            envelopes.Add(new
            {
                v = 1,
                batchId = Guid.NewGuid(),
                projectId,
                createdAt = DateTime.UtcNow.ToString("o"),
                eventCount = 1,
                alg = "RSA-OAEP-2048-SHA256+AES-256-GCM",
                wrappedKey = Convert.ToBase64String(wrappedKey),
                iv = Convert.ToBase64String(nonce),
                tag = Convert.ToBase64String(tag),
                ciphertext = Convert.ToBase64String(ciphertext),
            });
        }

        using var ingestRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingest/logs");
        ingestRequest.Headers.Add("X-Api-Key", apiKey);
        ingestRequest.Content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(new { envelopes }),
            System.Text.Encoding.UTF8,
            "application/json");

        var ingestResponse = await client.SendAsync(ingestRequest);
        ingestResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        return (client, projectId, logIds.ToArray());
    }

    [Fact]
    public async Task Query_returns_ingested_logs()
    {
        var (client, projectId, logIds) = await SetupWithLogsAsync();

        using var response = await client.GetAsync($"/api/v1/projects/{projectId}/logs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(ReadBody(response));
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(5);

        // Verify all log IDs are present.
        var returnedIds = items.Select(i => i.GetProperty("id").GetString()).ToList();
        foreach (var id in logIds)
        {
            returnedIds.Should().Contain(id);
        }
    }

    [Fact]
    public async Task Query_filters_by_level()
    {
        var (client, projectId, _) = await SetupWithLogsAsync();

        using var response = await client.GetAsync($"/api/v1/projects/{projectId}/logs?level=error");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(ReadBody(response));
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().NotBeEmpty();
        items.Should().OnlyContain(i => i.GetProperty("level").GetString() == "error");
    }

    [Fact]
    public async Task Query_filters_by_tag()
    {
        var (client, projectId, _) = await SetupWithLogsAsync();

        using var response = await client.GetAsync($"/api/v1/projects/{projectId}/logs?tag=test");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(ReadBody(response));
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().OnlyContain(i => i.GetProperty("tag").GetString() == "test");
    }

    [Fact]
    public async Task Query_returns_404_for_non_owner()
    {
        var (clientA, projectIdA, _) = await SetupWithLogsAsync();
        var (clientB, _, _) = await SetupWithLogsAsync();

        using var response = await clientB.GetAsync($"/api/v1/projects/{projectIdA}/logs");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Query_invalid_range_returns_400()
    {
        var (client, projectId, _) = await SetupWithLogsAsync();

        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow.AddDays(-2); // to before from

        using var response = await client.GetAsync(
            $"/api/v1/projects/{projectId}/logs?from={from:O}&to={to:O}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = JsonDocument.Parse(ReadBody(response));
        body.RootElement.GetProperty("code").GetString().Should().Be("validation_failed");
    }

    [Fact]
    public async Task Query_range_over_90_days_returns_400()
    {
        var (client, projectId, _) = await SetupWithLogsAsync();

        var from = DateTime.UtcNow.AddDays(-100);
        var to = DateTime.UtcNow;

        using var response = await client.GetAsync(
            $"/api/v1/projects/{projectId}/logs?from={from:O}&to={to:O}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = JsonDocument.Parse(ReadBody(response));
        body.RootElement.GetProperty("code").GetString().Should().Be("validation_failed");
    }

    [Fact]
    public async Task Query_pagination_returns_next_cursor()
    {
        var (client, projectId, _) = await SetupWithLogsAsync();

        using var response = await client.GetAsync($"/api/v1/projects/{projectId}/logs?limit=2");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(ReadBody(response));
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCount(2);

        var nextCursor = body.RootElement.GetProperty("nextCursor");
        nextCursor.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Query_uses_cursor_for_next_page()
    {
        var (client, projectId, _) = await SetupWithLogsAsync();

        // First page.
        using var response1 = await client.GetAsync($"/api/v1/projects/{projectId}/logs?limit=2");
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        var body1 = JsonDocument.Parse(ReadBody(response1));
        var items1 = body1.RootElement.GetProperty("items").EnumerateArray().ToList();
        items1.Should().HaveCount(2);
        var cursor1 = body1.RootElement.GetProperty("nextCursor").GetString();
        cursor1.Should().NotBeNullOrWhiteSpace();

        // Decode cursor to verify timestamp
        var cursorBytes = System.Convert.FromBase64String(cursor1!);
        var cursorText = System.Text.Encoding.UTF8.GetString(cursorBytes);
        var cursorParts = cursorText.Split('|');
        var cursorTs = DateTime.Parse(cursorParts[0], System.Globalization.CultureInfo.InvariantCulture);

        // Second page using cursor.
        using var response2 = await client.GetAsync(
            $"/api/v1/projects/{projectId}/logs?limit=2&cursor={Uri.EscapeDataString(cursor1!)}");
        response2.StatusCode.Should().Be(HttpStatusCode.OK);

        var body2 = JsonDocument.Parse(ReadBody(response2));
        var items2 = body2.RootElement.GetProperty("items").EnumerateArray().ToList();
        items2.Should().HaveCount(2);

        // Verify no overlap between pages.
        var ids1 = items1.Select(i => i.GetProperty("id").GetString()).ToList();
        var ids2 = items2.Select(i => i.GetProperty("id").GetString()).ToList();
        ids1.Should().NotContain(ids2, because: $"Cursor ts={cursorTs}, page1 ids={string.Join(",", ids1)}, page2 ids={string.Join(",", ids2)}");
    }
}
