using FluentValidation;
using FlutterLogs.Api.Common;
using FlutterLogs.Api.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace FlutterLogs.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth-register")
            .WithName("Register");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth-login")
            .WithName("Login");

        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .WithName("RefreshSession");

        group.MapPost("/logout", LogoutAsync)
            .AllowAnonymous()
            .WithName("Logout");

        group.MapGet("/me", MeAsync)
            .RequireAuthorization()
            .WithName("GetCurrentUser");

        group.MapPost("/verify-email", VerifyEmailAsync)
            .AllowAnonymous()
            .WithName("VerifyEmail");

        group.MapPost("/resend-verification", ResendVerificationAsync)
            .RequireAuthorization()
            .WithName("ResendVerification");

        group.MapPost("/forgot-password", ForgotPasswordAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth-forgot")
            .WithName("ForgotPassword");

        group.MapPost("/reset-password", ResetPasswordAsync)
            .AllowAnonymous()
            .WithName("ResetPassword");

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        RegisterRequestValidator validator,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var errors = Validate(validator, request);
        if (errors is not null)
        {
            return ApiResults.ValidationProblem(errors);
        }

        var result = await authService.RegisterAsync(request, cancellationToken);

        return result.IsSuccess
            ? Results.Created("/api/v1/auth/me", result.Value)
            : ApiResults.Problem(result.Error!);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        LoginRequestValidator validator,
        AuthService authService,
        HttpContext httpContext,
        IOptions<AuthOptions> options,
        CancellationToken cancellationToken)
    {
        var errors = Validate(validator, request);
        if (errors is not null)
        {
            return ApiResults.ValidationProblem(errors);
        }

        var result = await authService.LoginAsync(request, cancellationToken);

        if (!result.IsSuccess)
        {
            return ApiResults.Problem(result.Error!);
        }

        var session = result.Value!;
        RefreshCookie.Write(httpContext, options.Value, session.RefreshToken, session.RefreshExpiresAt);

        return Results.Ok(ToResponse(session));
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext httpContext,
        AuthService authService,
        IOptions<AuthOptions> options,
        CancellationToken cancellationToken)
    {
        var refreshToken = RefreshCookie.Read(httpContext, options.Value);

        if (!OriginValidator.IsAllowed(httpContext, options.Value))
        {
            return ApiResults.Problem(new ApiError(
                "invalid_origin",
                "The request origin is not allowed.",
                StatusCodes.Status403Forbidden));
        }

        if (string.IsNullOrEmpty(refreshToken))
        {
            return ApiResults.Problem(new ApiError(
                "missing_refresh_token",
                "The refresh token cookie is missing.",
                StatusCodes.Status401Unauthorized));
        }

        var result = await authService.RefreshAsync(refreshToken, cancellationToken);

        if (!result.IsSuccess)
        {
            RefreshCookie.Delete(httpContext, options.Value);
            return ApiResults.Problem(result.Error!);
        }

        var session = result.Value!;
        RefreshCookie.Write(httpContext, options.Value, session.RefreshToken, session.RefreshExpiresAt);

        return Results.Ok(ToResponse(session));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        AuthService authService,
        IOptions<AuthOptions> options,
        CancellationToken cancellationToken)
    {
        var refreshToken = RefreshCookie.Read(httpContext, options.Value);

        if (!OriginValidator.IsAllowed(httpContext, options.Value))
        {
            return ApiResults.Problem(new ApiError(
                "invalid_origin",
                "The request origin is not allowed.",
                StatusCodes.Status403Forbidden));
        }

        if (!string.IsNullOrEmpty(refreshToken))
        {
            await authService.LogoutAsync(refreshToken, cancellationToken);
        }

        RefreshCookie.Delete(httpContext, options.Value);

        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(
        HttpContext httpContext,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();

        if (userId is null)
        {
            return ApiResults.Problem(new ApiError(
                "unauthorized",
                "Authentication is required.",
                StatusCodes.Status401Unauthorized));
        }

        var user = await authService.FindUserAsync(userId.Value, cancellationToken);

        return user is null
            ? ApiResults.Problem(new ApiError("not_found", "Account not found.", StatusCodes.Status404NotFound))
            : Results.Ok(UserDto.FromEntity(user));
    }

    private static async Task<IResult> VerifyEmailAsync(
        VerifyEmailRequest request,
        VerifyEmailRequestValidator validator,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var errors = Validate(validator, request);
        if (errors is not null)
        {
            return ApiResults.ValidationProblem(errors);
        }

        var result = await authService.VerifyEmailAsync(request.Token, cancellationToken);

        return result.IsSuccess ? Results.NoContent() : ApiResults.Problem(result.Error!);
    }

    private static async Task<IResult> ResendVerificationAsync(
        HttpContext httpContext,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.User.GetUserId();

        if (userId is null)
        {
            return ApiResults.Problem(new ApiError(
                "unauthorized",
                "Authentication is required.",
                StatusCodes.Status401Unauthorized));
        }

        var result = await authService.ResendVerificationAsync(userId.Value, cancellationToken);

        return result.IsSuccess ? Results.Accepted() : ApiResults.Problem(result.Error!);
    }

    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        ForgotPasswordRequestValidator validator,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var errors = Validate(validator, request);
        if (errors is not null)
        {
            return ApiResults.ValidationProblem(errors);
        }

        // Uniform response: never reveal whether the address is registered.
        await authService.RequestPasswordResetAsync(request.Email, cancellationToken);

        return Results.Accepted();
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        ResetPasswordRequestValidator validator,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var errors = Validate(validator, request);
        if (errors is not null)
        {
            return ApiResults.ValidationProblem(errors);
        }

        var result = await authService.ResetPasswordAsync(request, cancellationToken);

        return result.IsSuccess ? Results.NoContent() : ApiResults.Problem(result.Error!);
    }

    private static AuthResponse ToResponse(AuthSession session) =>
        new(session.AccessToken, "Bearer", session.ExpiresInSeconds, session.User);

    private static Dictionary<string, string[]>? Validate<T>(
        IValidator<T> validator,
        T request)
    {
        var result = validator.Validate(request);

        if (result.IsValid)
        {
            return null;
        }

        return result.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).ToArray());
    }
}
