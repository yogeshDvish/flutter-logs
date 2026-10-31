using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Arovyx.SealedTrace.Tests.Api;

[Collection(ApiTestGroup.Name)]
public sealed class AuthEndpointTests(SealedTraceApiFactory factory)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static string ReadBody(HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync().Result;

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Register_creates_user_and_returns_201()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = UniqueEmail("freshuser"),
            password = "correct horse battery staple",
            name = "Fresh User",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Register_duplicate_email_returns_email_taken()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail("dup");

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "correct horse battery staple",
        });

        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "correct horse battery staple",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var document = JsonDocument.Parse(ReadBody(response));
        document.RootElement.GetProperty("code").GetString().Should().Be("email_taken");
    }

    [Fact]
    public async Task Register_invalid_email_returns_400()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "not-an-email",
            password = "short",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var document = JsonDocument.Parse(ReadBody(response));
        document.RootElement.GetProperty("code").GetString().Should().Be("validation_failed");
    }

    [Fact]
    public async Task Login_success_returns_jwt_and_refresh_cookie()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail("login");

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "correct horse battery staple",
            name = "Login User",
        });

        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "correct horse battery staple",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(ReadBody(response));
        document.RootElement.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        document.RootElement.GetProperty("tokenType").GetString().Should().Be("Bearer");
        document.RootElement.GetProperty("expiresIn").GetInt32().Should().Be(900);
        document.RootElement.GetProperty("user").GetProperty("email").GetString().Should().Be(email);

        response.Headers.Contains("Set-Cookie").Should().BeTrue();
    }

    [Fact]
    public async Task Login_wrong_password_returns_invalid_credentials()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail("wrongpw");

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "correct horse battery staple",
        });

        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "wrong password",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var document = JsonDocument.Parse(ReadBody(response));
        document.RootElement.GetProperty("code").GetString().Should().Be("invalid_credentials");
    }

    [Fact]
    public async Task Login_unknown_email_returns_invalid_credentials()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = UniqueEmail("nonexistent"),
            password = "whatever",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var document = JsonDocument.Parse(ReadBody(response));
        document.RootElement.GetProperty("code").GetString().Should().Be("invalid_credentials");
    }

    [Fact]
    public async Task Refresh_rotates_token_and_sets_new_cookie()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail("refresh");

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "correct horse battery staple",
        });

        using var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "correct horse battery staple",
        });

        var refreshCookie = loginResponse.Headers.GetValues("Set-Cookie")
            .First(c => c.Contains("st_refresh="));
        var refreshToken = refreshCookie.Split(';')[0].Split('=')[1];

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", $"st_refresh={refreshToken}");
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(ReadBody(response));
        document.RootElement.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();

        response.Headers.Contains("Set-Cookie").Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_missing_cookie_returns_401()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/v1/auth/refresh", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var document = JsonDocument.Parse(ReadBody(response));
        document.RootElement.GetProperty("code").GetString().Should().Be("invalid_refresh_token");
    }

    [Fact]
    public async Task Me_requires_authentication()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_clears_refresh_cookie()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail("logout");

        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "correct horse battery staple",
        });

        using var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email,
            password = "correct horse battery staple",
        });

        var loginBody = JsonDocument.Parse(ReadBody(loginResponse));
        var accessToken = loginBody.RootElement.GetProperty("accessToken").GetString()!;

        var cookieHeader = loginResponse.Headers.GetValues("Set-Cookie").First();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Headers.Add("Cookie", cookieHeader);
        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
