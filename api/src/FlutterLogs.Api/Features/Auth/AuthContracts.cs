using FlutterLogs.Api.Infrastructure.Persistence.Entities;

namespace FlutterLogs.Api.Features.Auth;

public sealed record RegisterRequest(string Email, string Password, string? DisplayName);

public sealed record LoginRequest(string Email, string Password);

public sealed record VerifyEmailRequest(string Token);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResendVerificationRequest(string? Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record UserDto(
    Guid Id,
    string Email,
    string? DisplayName,
    bool EmailVerified,
    DateTime CreatedAt)
{
    public static UserDto FromEntity(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new UserDto(
            user.Id,
            user.Email,
            user.DisplayName,
            user.EmailVerifiedAt is not null,
            // Values read from SQL Server come back with Unspecified kind; they are UTC.
            DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc));
    }
}

public sealed record AuthResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    UserDto User);
