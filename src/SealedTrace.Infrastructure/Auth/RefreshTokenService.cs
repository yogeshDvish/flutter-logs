using System.Security.Cryptography;
using System.Text;
using Arovyx.SealedTrace.Core.Entities;
using Arovyx.SealedTrace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arovyx.SealedTrace.Infrastructure.Auth;

public sealed class RefreshTokenService
{
    private readonly SealedTraceDbContext _db;

    public RefreshTokenService(SealedTraceDbContext db)
    {
        _db = db;
    }

    public static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    public static byte[] HashToken(string token)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    public async Task<(string Plaintext, RefreshToken Entity)> CreateAsync(
        Guid userId,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        var plaintext = GenerateToken();
        var tokenHash = HashToken(plaintext);

        var entity = new RefreshToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAtUtc = expiresAt,
        };

        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return (plaintext, entity);
    }

    public sealed class RotationResult
    {
        public bool Success { get; init; }
        public Guid UserId { get; init; }
        public string NewTokenPlaintext { get; init; } = string.Empty;
        public DateTime NewExpiresAt { get; init; }
    }

    public async Task<RotationResult> ValidateAndRotateAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(refreshToken);

        var existing = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash.SequenceEqual(tokenHash), cancellationToken);

        if (existing is null)
        {
            return new RotationResult { Success = false };
        }

        if (existing.RevokedAtUtc is not null)
        {
            // Reuse of a revoked token: revoke all tokens for this user (token reuse detection).
            await RevokeAllForUserAsync(existing.UserId, cancellationToken);
            return new RotationResult { Success = false };
        }

        if (existing.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return new RotationResult { Success = false };
        }

        // Rotate: revoke the old token and issue a new one.
        existing.RevokedAtUtc = DateTime.UtcNow;
        existing.ReplacedByTokenHash = HashToken(GenerateToken());

        var (newTokenPlain, newEntity) = await CreateAsync(existing.UserId, DateTime.UtcNow.AddDays(30), cancellationToken);

        return new RotationResult
        {
            Success = true,
            UserId = existing.UserId,
            NewTokenPlaintext = newTokenPlain,
            NewExpiresAt = newEntity.ExpiresAtUtc,
        };
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(refreshToken);

        var existing = await _db.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.TokenHash.SequenceEqual(tokenHash), cancellationToken);

        if (existing is not null && existing.RevokedAtUtc is null)
        {
            existing.RevokedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var tokens = await _db.RefreshTokens
            .Where(rt => rt.UserId == userId && rt.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var expired = await _db.RefreshTokens
            .Where(rt => rt.ExpiresAtUtc <= DateTime.UtcNow || rt.RevokedAtUtc != null)
            .ToListAsync(cancellationToken);

        _db.RefreshTokens.RemoveRange(expired);
        return await _db.SaveChangesAsync(cancellationToken);
    }
}
