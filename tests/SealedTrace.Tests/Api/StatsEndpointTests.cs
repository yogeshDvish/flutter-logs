using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Arovyx.SealedTrace.Tests.Api;

[Collection(ApiTestGroup.Name)]
public sealed class StatsEndpointTests(SealedTraceApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static string ReadBody(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync().Result;

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private async Task<(HttpClient Client, Guid ProjectId)> SetupWithLogsAsync()
    {
        var client = factory.CreateClient();
        var email = UniqueEmail("stats");

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "correct horse battery staple",
            name = "Stats User",
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
            name = $"Stats Test Project {Guid.NewGuid():N}",
            environment = "prod",
        });
        projectResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var projectBody = JsonDocument.Parse(ReadBody(projectResponse));
        var projectId = projectBody.RootElement.GetProperty("project").GetProperty("id").GetGuid();

        // Create API key and ingest logs.
        var keyResponse = await client.PostAsync($"/api/v1/projects/{projectId}/api-keys", null);
        keyResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var keyBody = JsonDocument.Parse(ReadBody(keyResponse));
        var apiKey = keyBody.RootElement.GetProperty("plaintext").GetString()!;

        using var rsa = System.Security.Cryptography.RSA.Create();
        rsa.ImportFromPem(projectBody.RootElement.GetProperty("publicKeyPem").GetString()!);

        var envelopes = new List<object>();

        for (var i = 0; i < 3; i++)
        {
            var logEvent = new
            {
                id = Guid.NewGuid(),
                seq = i + 1,
                ts = DateTime.UtcNow.AddMinutes(-i).ToString("o"),
                level = i switch
                {
                    0 => "info",
                    1 => "error",
                    _ => "warning",
                },
                tag = "stats",
                message = $"Stats log {i}",
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

        return (client, projectId);
    }

    [Fact]
    public async Task Stats_returns_aggregated_data()
    {
        var (client, projectId) = await SetupWithLogsAsync();

        using var response = await client.GetAsync($"/api/v1/projects/{projectId}/stats");
        var bodyText = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"STATS RESPONSE: {response.StatusCode} {bodyText}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: bodyText);

        var body = JsonDocument.Parse(bodyText);
        body.RootElement.GetProperty("totalEvents").GetInt64().Should().Be(3);
        body.RootElement.GetProperty("levels").EnumerateArray().Should().NotBeEmpty();
        body.RootElement.GetProperty("eventsPerDay").EnumerateArray().Should().NotBeEmpty();
        body.RootElement.GetProperty("topTags").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Stats_level_distribution_matches()
    {
        var (client, projectId) = await SetupWithLogsAsync();

        using var response = await client.GetAsync($"/api/v1/projects/{projectId}/stats");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(ReadBody(response));
        var levels = body.RootElement.GetProperty("levels").EnumerateArray().ToList();

        // We ingested 1 info, 1 error, 1 warning.
        levels.Should().Contain(l => l.GetProperty("name").GetString() == "info" && l.GetProperty("count").GetInt64() == 1);
        levels.Should().Contain(l => l.GetProperty("name").GetString() == "error" && l.GetProperty("count").GetInt64() == 1);
        levels.Should().Contain(l => l.GetProperty("name").GetString() == "warning" && l.GetProperty("count").GetInt64() == 1);
    }

    [Fact]
    public async Task Stats_returns_404_for_non_owner()
    {
        var (clientA, projectIdA) = await SetupWithLogsAsync();
        var (clientB, _) = await SetupWithLogsAsync();

        using var response = await clientB.GetAsync($"/api/v1/projects/{projectIdA}/stats");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stats_invalid_range_returns_400()
    {
        var (client, projectId) = await SetupWithLogsAsync();

        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow.AddDays(-2);

        using var response = await client.GetAsync(
            $"/api/v1/projects/{projectId}/stats?from={from:O}&to={to:O}");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = JsonDocument.Parse(ReadBody(response));
        body.RootElement.GetProperty("code").GetString().Should().Be("validation_failed");
    }
}
