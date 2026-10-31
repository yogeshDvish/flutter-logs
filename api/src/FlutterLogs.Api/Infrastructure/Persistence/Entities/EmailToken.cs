namespace FlutterLogs.Api.Infrastructure.Persistence.Entities;

public enum EmailTokenPurpose : byte
{
    VerifyEmail = 0,
    ResetPassword = 1,
}

/// <summary>Single-use, hashed token backing email verification and password reset.</summary>
public sealed class EmailToken
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public EmailTokenPurpose Purpose { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? UsedAt { get; set; }

    public User? User { get; set; }
}
