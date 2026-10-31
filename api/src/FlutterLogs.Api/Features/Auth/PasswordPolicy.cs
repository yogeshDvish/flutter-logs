namespace FlutterLogs.Api.Features.Auth;

public static class PasswordPolicy
{
    public const int MinimumLength = 12;

    public const int MaximumLength = 128;

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456789012",
        "password1234",
        "password12345",
        "qwertyuiop12",
        "letmein12345",
        "admin1234567",
        "welcome12345",
        "iloveyou1234",
        "changeme1234",
        "flutterlogs1",
    };

    public static bool IsCommon(string? password) =>
        !string.IsNullOrEmpty(password) && CommonPasswords.Contains(password);

    public static bool IsAcceptable(string? password) =>
        !string.IsNullOrEmpty(password)
        && password.Length is >= MinimumLength and <= MaximumLength
        && !IsCommon(password);
}
