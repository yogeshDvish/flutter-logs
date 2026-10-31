namespace FlutterLogs.Api.Features.Auth;

public sealed record AuthSession(
    string AccessToken,
    int ExpiresInSeconds,
    string RefreshToken,
    DateTime RefreshExpiresAt,
    UserDto User);
