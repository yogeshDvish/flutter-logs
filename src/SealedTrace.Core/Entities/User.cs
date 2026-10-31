namespace Arovyx.SealedTrace.Core.Entities;

public sealed class User
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string? Name { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; } = new List<RefreshToken>();

    public ICollection<Project> Projects { get; } = new List<Project>();
}
