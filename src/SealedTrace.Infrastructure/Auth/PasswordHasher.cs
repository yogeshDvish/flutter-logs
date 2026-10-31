namespace Arovyx.SealedTrace.Infrastructure.Auth;

/// <summary>
/// BCrypt password hashing with work factor 12, as specified by the implementation plan.
/// </summary>
public static class PasswordHasher
{
    private const int WorkFactor = 12;

    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public static bool Verify(string password, string hash) =>
        BCrypt.Net.BCrypt.Verify(password, hash);
}
