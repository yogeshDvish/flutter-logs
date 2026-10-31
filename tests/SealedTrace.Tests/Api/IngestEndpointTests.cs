using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Arovyx.SealedTrace.Core.Dtos.Ingest;

namespace Arovyx.SealedTrace.Tests.Api;

[Collection(ApiTestGroup.Name)]
public sealed class IngestEndpointTests(SealedTraceApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static string ReadBody(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync().Result;

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    private async Task<(HttpClient Client, string ApiKey, Guid ProjectId, string PublicKeyPem)> SetupProjectAsync()
    {
        var client = factory.CreateClient();
        var email = UniqueEmail("ingest");

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "correct horse battery staple",
            name = "Ingest User",
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
            name = $"Ingest Test Project {Guid.NewGuid():N}",
            environment = "prod",
        });
        projectResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var projectBody = JsonDocument.Parse(ReadBody(projectResponse));
        var apiKeyPlaintext = projectBody.RootElement.GetProperty("apiKey").GetProperty("plaintext").GetString()!;
        var projectId = projectBody.RootElement.GetProperty("project").GetProperty("id").GetGuid();
        var publicKeyPem = projectBody.RootElement.GetProperty("publicKeyPem").GetString()!;

        return (client, apiKeyPlaintext, projectId, publicKeyPem);
    }

    private static SealedEnvelope CreateSealedEnvelope(
        string publicKeyPem,
        Guid projectId,
        int eventCount = 1,
        string? algorithm = null,
        bool tamperCiphertext = false)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);

        var logEvents = new[]
        {
            new
            {
                id = Guid.NewGuid(),
                seq = 1,
                ts = DateTime.UtcNow.ToString("o"),
                level = "info",
                tag = "test",
                message = "Hello, sealed world!",
            },
        };

        var eventsJson = JsonSerializer.Serialize(logEvents);
        var eventsBytes = Encoding.UTF8.GetBytes(eventsJson);

        var aesKey = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[eventsBytes.Length];
        var tag = new byte[16];

        using (var aesGcm = new AesGcm(aesKey, 16))
        {
            aesGcm.Encrypt(nonce, eventsBytes, ciphertext, tag);
        }

        if (tamperCiphertext)
        {
            ciphertext[ciphertext.Length - 1] ^= 0xFF;
        }

        var wrappedKey = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);

        return new SealedEnvelope
        {
            V = 1,
            BatchId = Guid.NewGuid(),
            ProjectId = projectId,
            CreatedAt = DateTime.UtcNow,
            EventCount = eventCount,
            Alg = algorithm ?? "RSA-OAEP-2048-SHA256+AES-256-GCM",
            WrappedKey = Convert.ToBase64String(wrappedKey),
            Iv = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Ciphertext = Convert.ToBase64String(ciphertext),
        };
    }

    [Fact]
    public async Task Valid_ingest_returns_202_accepted()
    {
        var (client, apiKey, projectId, publicKeyPem) = await SetupProjectAsync();

        var envelope = CreateSealedEnvelope(publicKeyPem, projectId);
        var batchId = envelope.BatchId;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingest/logs");
        request.Headers.Add("X-Api-Key", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { envelopes = new[] { envelope } }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);

        var respBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, because: respBody);

        var body = JsonDocument.Parse(respBody);
        body.RootElement.GetProperty("accepted").EnumerateArray()
            .Should().ContainSingle(b => b.GetString() == batchId.ToString());
        body.RootElement.GetProperty("duplicate").EnumerateArray().Should().BeEmpty();
        body.RootElement.GetProperty("rejected").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_batch_returns_duplicate()
    {
        var (client, apiKey, projectId, publicKeyPem) = await SetupProjectAsync();

        var envelope = CreateSealedEnvelope(publicKeyPem, projectId);
        var batchId = envelope.BatchId;

        // First ingest.
        using var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingest/logs");
        request1.Headers.Add("X-Api-Key", apiKey);
        request1.Content = new StringContent(
            JsonSerializer.Serialize(new { envelopes = new[] { envelope } }),
            Encoding.UTF8,
            "application/json");

        using var response1 = await client.SendAsync(request1);
        response1.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Second ingest with same batch ID.
        using var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingest/logs");
        request2.Headers.Add("X-Api-Key", apiKey);
        request2.Content = new StringContent(
            JsonSerializer.Serialize(new { envelopes = new[] { envelope } }),
            Encoding.UTF8,
            "application/json");

        using var response2 = await client.SendAsync(request2);
        response2.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = JsonDocument.Parse(ReadBody(response2));
        body.RootElement.GetProperty("duplicate").EnumerateArray()
            .Should().ContainSingle(b => b.GetString() == batchId.ToString());
        body.RootElement.GetProperty("accepted").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Wrong_algorithm_returns_rejection()
    {
        var (client, apiKey, projectId, publicKeyPem) = await SetupProjectAsync();

        var envelope = CreateSealedEnvelope(publicKeyPem, projectId, algorithm: "WRONG-ALG");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingest/logs");
        request.Headers.Add("X-Api-Key", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { envelopes = new[] { envelope } }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = JsonDocument.Parse(ReadBody(response));
        body.RootElement.GetProperty("rejected").EnumerateArray()
            .Should().ContainSingle(r => r.GetProperty("reason").GetString() == "invalid_algorithm");
    }

    [Fact]
    public async Task Project_mismatch_returns_rejection()
    {
        var (client, apiKey, projectId, publicKeyPem) = await SetupProjectAsync();

        // Create envelope with a different project ID.
        var envelope = CreateSealedEnvelope(publicKeyPem, Guid.NewGuid());

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingest/logs");
        request.Headers.Add("X-Api-Key", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { envelopes = new[] { envelope } }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = JsonDocument.Parse(ReadBody(response));
        body.RootElement.GetProperty("rejected").EnumerateArray()
            .Should().ContainSingle(r => r.GetProperty("reason").GetString() == "project_mismatch");
    }

    [Fact]
    public async Task Missing_api_key_returns_401()
    {
        var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/ingest/logs", new
        {
            envelopes = Array.Empty<SealedEnvelope>(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoked_api_key_returns_403()
    {
        var (client, apiKey, projectId, publicKeyPem) = await SetupProjectAsync();

        // Revoke the API key first (need to get the key ID).
        var jwt = client.DefaultRequestHeaders.Authorization?.Parameter ?? string.Empty;
        var listResponse = await client.GetAsync($"/api/v1/projects/{projectId}/api-keys");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listBody = JsonDocument.Parse(ReadBody(listResponse));
        var keyId = listBody.RootElement.EnumerateArray().First().GetProperty("id").GetGuid();

        var revokeResponse = await client.DeleteAsync($"/api/v1/projects/{projectId}/api-keys/{keyId}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Try ingest with revoked key.
        var envelope = CreateSealedEnvelope(publicKeyPem, projectId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingest/logs");
        request.Headers.Add("X-Api-Key", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { envelopes = new[] { envelope } }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Future_timestamp_returns_rejection()
    {
        var (client, apiKey, projectId, publicKeyPem) = await SetupProjectAsync();

        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);

        var logEvents = new[]
        {
            new
            {
                id = Guid.NewGuid(),
                seq = 1,
                ts = DateTime.UtcNow.AddMinutes(10).ToString("o"), // 10 minutes in the future
                level = "info",
                tag = "test",
                message = "Future log",
            },
        };

        var eventsJson = JsonSerializer.Serialize(logEvents);
        var eventsBytes = Encoding.UTF8.GetBytes(eventsJson);

        var aesKey = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[eventsBytes.Length];
        var tag = new byte[16];

        using (var aesGcm = new AesGcm(aesKey, 16))
        {
            aesGcm.Encrypt(nonce, eventsBytes, ciphertext, tag);
        }

        var wrappedKey = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);

        var envelope = new SealedEnvelope
        {
            V = 1,
            BatchId = Guid.NewGuid(),
            ProjectId = projectId,
            CreatedAt = DateTime.UtcNow,
            EventCount = 1,
            Alg = "RSA-OAEP-2048-SHA256+AES-256-GCM",
            WrappedKey = Convert.ToBase64String(wrappedKey),
            Iv = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Ciphertext = Convert.ToBase64String(ciphertext),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingest/logs");
        request.Headers.Add("X-Api-Key", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { envelopes = new[] { envelope } }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = JsonDocument.Parse(ReadBody(response));
        body.RootElement.GetProperty("rejected").EnumerateArray()
            .Should().ContainSingle(r => r.GetProperty("reason").GetString() == "timestamp_in_future");
    }

    [Fact]
    public async Task Tampered_ciphertext_returns_decrypt_failed()
    {
        var (client, apiKey, projectId, publicKeyPem) = await SetupProjectAsync();

        var envelope = CreateSealedEnvelope(publicKeyPem, projectId, tamperCiphertext: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ingest/logs");
        request.Headers.Add("X-Api-Key", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { envelopes = new[] { envelope } }),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = JsonDocument.Parse(ReadBody(response));
        body.RootElement.GetProperty("rejected").EnumerateArray()
            .Should().ContainSingle(r => r.GetProperty("reason").GetString() == "decrypt_failed");
    }
}
