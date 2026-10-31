using System.Security.Cryptography;
using System.Text;
using Arovyx.SealedTrace.Core.Dtos.ApiKeys;
using Arovyx.SealedTrace.Core.Entities;
using Arovyx.SealedTrace.Core.Interfaces;
using Arovyx.SealedTrace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arovyx.SealedTrace.Infrastructure.Services;

public sealed class ApiKeyService : IApiKeyService
{
    private const int MaxActiveKeysPerProject = 5;

    private readonly SealedTraceDbContext _db;

    public ApiKeyService(SealedTraceDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ApiKeyDto>> ListApiKeysAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var project = await _db.Projects
            .Where(p => p.Id == projectId && p.OwnerId == ownerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            throw new KeyNotFoundException($"Project '{projectId}' not found or not owned by the caller.");
        }

        return await _db.ApiKeys
            .Where(k => k.ProjectId == projectId)
            .OrderByDescending(k => k.CreatedAtUtc)
            .Select(k => new ApiKeyDto
            {
                Id = k.Id,
                Prefix = k.KeyPrefix,
                CreatedAtUtc = k.CreatedAtUtc,
                RevokedAtUtc = k.RevokedAtUtc,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<ApiKeyCreatedDto> CreateApiKeyAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var project = await _db.Projects
            .Where(p => p.Id == projectId && p.OwnerId == ownerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            throw new KeyNotFoundException($"Project '{projectId}' not found or not owned by the caller.");
        }

        var activeKeyCount = await _db.ApiKeys
            .CountAsync(k => k.ProjectId == projectId && k.RevokedAtUtc == null, cancellationToken);

        if (activeKeyCount >= MaxActiveKeysPerProject)
        {
            throw new KeyLimitReachedException(MaxActiveKeysPerProject);
        }

        var (plaintext, hash, prefix) = GenerateApiKey();

        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            KeyHash = hash,
            KeyPrefix = prefix,
            CreatedAtUtc = DateTime.UtcNow,
        };

        _db.ApiKeys.Add(apiKey);
        await _db.SaveChangesAsync(cancellationToken);

        return new ApiKeyCreatedDto
        {
            Id = apiKey.Id,
            Prefix = apiKey.KeyPrefix,
            Plaintext = plaintext,
            CreatedAtUtc = apiKey.CreatedAtUtc,
        };
    }

    public async Task RevokeApiKeyAsync(
        Guid projectId,
        Guid keyId,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var project = await _db.Projects
            .Where(p => p.Id == projectId && p.OwnerId == ownerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            throw new KeyNotFoundException($"Project '{projectId}' not found or not owned by the caller.");
        }

        var apiKey = await _db.ApiKeys
            .FirstOrDefaultAsync(k => k.Id == keyId && k.ProjectId == projectId, cancellationToken);

        if (apiKey is null)
        {
            throw new KeyNotFoundException($"API key '{keyId}' not found.");
        }

        apiKey.RevokedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static (string plaintext, byte[] hash, string prefix) GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var base64 = Convert.ToBase64String(bytes);
        var plaintext = base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(plaintext));
        var prefix = plaintext.Length >= 8 ? plaintext[..8] : plaintext;

        return (plaintext, hash, prefix);
    }
}

public sealed class KeyLimitReachedException : Exception
{
    public KeyLimitReachedException(int maxKeys) : base($"Maximum of {maxKeys} active API keys per project reached.") { }
}
