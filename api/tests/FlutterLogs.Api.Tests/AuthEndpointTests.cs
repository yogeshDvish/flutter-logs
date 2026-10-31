using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace FlutterLogs.Api.Tests;

[Collection(ApiTestGroup.Name)]
public sealed class AuthEndpointTests(ApiFactory factory)
{
    private const string StrongPassword = "CorrectHorseBattery9!";
    private const string NewPassword = "AnotherStrongPass7!";

    [Fact]
    public async Task Register_creates_unverified_account_without_password_material()
    {
        using var client = CreateClient();
        var email = $"  MixedCase-{Guid.NewGuid():N}@Example.COM ";

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password = StrongPassword, displayName = "Ana" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var raw = await response.Content.ReadAsStringAsync();
        raw.ShouldNotContain("argon2", Case.Insensitive);
        raw.ShouldNotContain(StrongPassword, Case.Sensitive);

        var user = await response.Content.ReadFromJsonAsync<UserPayload>();
        user.ShouldNotBeNull();
        user.Email.ShouldBe(email.Trim().ToLowerInvariant());
        user.DisplayName.ShouldBe("Ana");
        user.EmailVerified.ShouldBeFalse();
    }

    [Fact]
    public async Task Register_rejects_duplicate_email()
    {
        using var client = CreateClient();
        var email = NewEmail();

        var first = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password = StrongPassword });
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var duplicate = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password = StrongPassword });

        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblemAsync(duplicate)).Code.ShouldBe("email_taken");
    }

    [Fact]
    public async Task Register_rejects_weak_password_with_field_errors()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email = NewEmail(), password = "short" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemPayload>();
        problem.ShouldNotBeNull();
        problem.Code.ShouldBe("validation_failed");
        problem.Errors.ShouldContainKey("Password");
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_uniform_credentials_error()
    {
        using var client = CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email, StrongPassword);

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = "WrongPassword123!" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblemAsync(response)).Code.ShouldBe("invalid_credentials");
    }

    [Fact]
    public async Task Login_for_unknown_email_returns_the_same_error()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = NewEmail(), password = StrongPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblemAsync(response)).Code.ShouldBe("invalid_credentials");
    }

    [Fact]
    public async Task Me_requires_authentication_and_returns_the_owner_when_authenticated()
    {
        using var client = CreateClient();
        var email = NewEmail();
        var (_, session) = await RegisterAndLoginAsync(client, email, StrongPassword);

        var anonymous = await client.GetAsync("/api/v1/auth/me");
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        anonymous.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await ReadProblemAsync(anonymous)).Code.ShouldBe("unauthorized");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var authenticated = await client.SendAsync(request);
        authenticated.StatusCode.ShouldBe(HttpStatusCode.OK);

        var user = await authenticated.Content.ReadFromJsonAsync<UserPayload>();
        user.ShouldNotBeNull();
        user.Email.ShouldBe(email);
    }

    [Fact]
    public async Task Refresh_rotates_the_cookie_and_reuse_revokes_the_whole_family()
    {
        using var client = CreateClient(handleCookies: false);
        var email = NewEmail();
        var (login, _) = await RegisterAndLoginAsync(client, email, StrongPassword);

        var originalCookie = GetRefreshCookie(login);
        originalCookie.ShouldNotBeNullOrWhiteSpace();

        var rotated = await SendWithCookieAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/refresh",
            originalCookie!);

        rotated.StatusCode.ShouldBe(HttpStatusCode.OK);

        var rotatedCookie = GetRefreshCookie(rotated);
        rotatedCookie.ShouldNotBeNullOrWhiteSpace();
        rotatedCookie.ShouldNotBe(originalCookie);

        var reuse = await SendWithCookieAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/refresh",
            originalCookie!);

        reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadProblemAsync(reuse)).Code.ShouldBe("token_reuse_detected");

        var afterReuse = await SendWithCookieAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/refresh",
            rotatedCookie!);

        afterReuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_rejects_a_foreign_origin()
    {
        using var client = CreateClient(handleCookies: false);
        var email = NewEmail();
        var (login, _) = await RegisterAndLoginAsync(client, email, StrongPassword);
        var cookie = GetRefreshCookie(login);
        cookie.ShouldNotBeNullOrWhiteSpace();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", $"fl_refresh={cookie}");
        request.Headers.Add("Origin", "https://evil.example");

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadProblemAsync(response)).Code.ShouldBe("invalid_origin");
    }

    [Fact]
    public async Task Refresh_accepts_the_configured_dashboard_origin()
    {
        using var client = CreateClient(handleCookies: false);
        var email = NewEmail();
        var (login, _) = await RegisterAndLoginAsync(client, email, StrongPassword);
        var cookie = GetRefreshCookie(login);
        cookie.ShouldNotBeNullOrWhiteSpace();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", $"fl_refresh={cookie}");
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_revokes_the_session()
    {
        using var client = CreateClient(handleCookies: false);
        var email = NewEmail();
        var (login, _) = await RegisterAndLoginAsync(client, email, StrongPassword);
        var cookie = GetRefreshCookie(login);
        cookie.ShouldNotBeNullOrWhiteSpace();

        var logout = await SendWithCookieAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/logout",
            cookie!);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var refresh = await SendWithCookieAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/refresh",
            cookie!);

        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Verify_email_marks_the_account_verified_and_is_single_use()
    {
        using var client = CreateClient();
        var email = NewEmail();
        await RegisterAsync(client, email, StrongPassword);

        var token = factory.Emails.GetVerificationToken(email);
        token.ShouldNotBeNullOrWhiteSpace();

        var verify = await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { token });
        verify.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = StrongPassword });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);

        var session = await login.Content.ReadFromJsonAsync<AuthPayload>();
        session.ShouldNotBeNull();
        session.User.EmailVerified.ShouldBeTrue();

        var secondUse = await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { token });
        secondUse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadProblemAsync(secondUse)).Code.ShouldBe("token_already_used");
    }

    [Fact]
    public async Task Forgot_password_never_reveals_whether_an_account_exists()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/forgot-password",
            new { email = NewEmail() });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task Reset_password_changes_credentials_and_revokes_existing_sessions()
    {
        using var client = CreateClient(handleCookies: false);
        var email = NewEmail();
        var (login, _) = await RegisterAndLoginAsync(client, email, StrongPassword);
        var sessionCookie = GetRefreshCookie(login);
        sessionCookie.ShouldNotBeNullOrWhiteSpace();

        var forgot = await client.PostAsJsonAsync(
            "/api/v1/auth/forgot-password",
            new { email });
        forgot.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var token = factory.Emails.GetResetToken(email);
        token.ShouldNotBeNullOrWhiteSpace();

        var reset = await client.PostAsJsonAsync(
            "/api/v1/auth/reset-password",
            new { token, newPassword = NewPassword });
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var oldPassword = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = StrongPassword });
        oldPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var newPasswordLogin = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password = NewPassword });
        newPasswordLogin.StatusCode.ShouldBe(HttpStatusCode.OK);

        var staleSession = await SendWithCookieAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/refresh",
            sessionCookie!);
        staleSession.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private HttpClient CreateClient(bool handleCookies = true) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = handleCookies,
        });

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static async Task RegisterAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new { email, password, displayName = "Test User" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static async Task<(HttpResponseMessage Login, AuthPayload Session)> RegisterAndLoginAsync(
        HttpClient client,
        string email,
        string password)
    {
        await RegisterAsync(client, email, password);

        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email, password });

        login.StatusCode.ShouldBe(HttpStatusCode.OK);

        var session = await login.Content.ReadFromJsonAsync<AuthPayload>();
        session.ShouldNotBeNull();

        return (login, session);
    }

    private static Task<HttpResponseMessage> SendWithCookieAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string refreshToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", $"fl_refresh={refreshToken}");
        return client.SendAsync(request);
    }

    private static string? GetRefreshCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            var firstSegment = value.Split(';', 2)[0];
            var separator = firstSegment.IndexOf('=', StringComparison.Ordinal);

            if (separator > 0 && firstSegment[..separator] == "fl_refresh")
            {
                return firstSegment[(separator + 1)..];
            }
        }

        return null;
    }

    private static async Task<ProblemPayload> ReadProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>();
        problem.ShouldNotBeNull();
        return problem;
    }

    private sealed record UserPayload(
        Guid Id,
        string Email,
        string? DisplayName,
        bool EmailVerified,
        DateTime CreatedAt);

    private sealed record AuthPayload(
        string AccessToken,
        string TokenType,
        int ExpiresIn,
        UserPayload User);

    private sealed record ProblemPayload(string Title, int Status, string Code);

    private sealed record ValidationProblemPayload(
        string Title,
        int Status,
        string Code,
        Dictionary<string, string[]> Errors);
}
