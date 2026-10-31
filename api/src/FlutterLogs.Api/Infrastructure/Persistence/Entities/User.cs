namespace FlutterLogs.Api.Infrastructure.Persistence.Entities;

public enum UserStatus : byte
{
    Active = 0,
    Disabled = 1,
}

/// <summary>A dashboard account. One user owns zero or more projects.</summary>
public sealed class User
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Always stored trimmed and lower-cased.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Argon2id PHC string; parameters are embedded.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public DateTime? EmailVerifiedAt { get; set; }

    public UserStatus Status { get; set; } = UserStatus.Active;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; } = new List<RefreshToken>();

    public ICollection<EmailToken> EmailTokens { get; } = new List<EmailToken>();
}
