using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Arovyx.SealedTrace.Core.Dtos.ApiKeys;
using Arovyx.SealedTrace.Core.Dtos.Projects;
using Arovyx.SealedTrace.Core.Entities;
using Arovyx.SealedTrace.Core.Interfaces;
using Arovyx.SealedTrace.Infrastructure.Caching;
using Arovyx.SealedTrace.Infrastructure.Crypto;
using Arovyx.SealedTrace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arovyx.SealedTrace.Infrastructure.Services;

public sealed class ProjectService : IProjectService
{
    private readonly SealedTraceDbContext _db;
    private readonly IProjectKeyProtector _keyProtector;
    private readonly MasterKeyRing _masterKeyRing;
    private readonly ProjectKeyCache _keyCache;

    public ProjectService(
        SealedTraceDbContext db,
        IProjectKeyProtector keyProtector,
        MasterKeyRing masterKeyRing,
        ProjectKeyCache keyCache)
    {
        _db = db;
        _keyProtector = keyProtector;
        _masterKeyRing = masterKeyRing;
        _keyCache = keyCache;
    }

    public async Task<(ProjectCreatedDto Created, string ApiKeyPlaintext)> CreateProjectAsync(
        Guid ownerId,
        CreateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Project name is required.", nameof(request));
        }

        var slug = await GenerateUniqueSlugAsync(request.Name, cancellationToken);

        using var rsa = RSA.Create(2048);
        var publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
        var privateKeyPem = rsa.ExportRSAPrivateKeyPem();

        var encryptedPrivateKey = _keyProtector.EncryptPrivateKey(privateKeyPem, _masterKeyRing.CurrentVersion);

        var project = new Project
        {
            Id = Guid.NewGuid(),
            OwnerId = ownerId,
            Name = request.Name.Trim(),
            Slug = slug,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Environment = string.IsNullOrWhiteSpace(request.Environment) ? "prod" : request.Environment.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        };

        var projectKey = new ProjectKey
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            Algorithm = "RSA-OAEP-2048-SHA256",
            PublicKeyPem = publicKeyPem,
            PrivateKeyEncrypted = encryptedPrivateKey,
            MasterKeyVersion = _masterKeyRing.CurrentVersion,
            CreatedAtUtc = DateTime.UtcNow,
        };

        var (apiKeyPlaintext, apiKeyHash, apiKeyPrefix) = GenerateApiKey();
        var apiKey = new ApiKey
        {
            Id = Guid.NewGuid(),
            ProjectId = project.Id,
            KeyHash = apiKeyHash,
            KeyPrefix = apiKeyPrefix,
            CreatedAtUtc = DateTime.UtcNow,
        };

        project.ProjectKey = projectKey;
        project.ApiKeys.Add(apiKey);

        _db.Projects.Add(project);
        await _db.SaveChangesAsync(cancellationToken);

        _keyCache.Set(project.Id, privateKeyPem);

        var dto = new ProjectDto
        {
            Id = project.Id,
            Name = project.Name,
            Slug = project.Slug,
            Description = project.Description,
            Environment = project.Environment,
            CreatedAtUtc = project.CreatedAtUtc,
            RetentionMaxAgeDays = project.RetentionMaxAgeDays,
            RetentionMaxEvents = project.RetentionMaxEvents,
            ActiveApiKeyCount = 1,
        };

        return (new ProjectCreatedDto
        {
            Project = dto,
            ApiKey = new ApiKeyCreatedDto
            {
                Id = apiKey.Id,
                Prefix = apiKey.KeyPrefix,
                Plaintext = apiKeyPlaintext,
                CreatedAtUtc = apiKey.CreatedAtUtc,
            },
            PublicKeyPem = publicKeyPem,
        }, apiKeyPlaintext);
    }

    public async Task<IReadOnlyList<ProjectDto>> ListProjectsAsync(
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var todayStart = DateTime.UtcNow.Date;

        var projects = await _db.Projects
            .Where(p => p.OwnerId == ownerId)
            .Select(p => new ProjectDto
            {
                Id = p.Id,
                Name = p.Name,
                Slug = p.Slug,
                Description = p.Description,
                Environment = p.Environment,
                CreatedAtUtc = p.CreatedAtUtc,
                RetentionMaxAgeDays = p.RetentionMaxAgeDays,
                RetentionMaxEvents = p.RetentionMaxEvents,
                ActiveApiKeyCount = p.ApiKeys.Count(k => k.RevokedAtUtc == null),
                TodayEventCount = p.Logs.Count(l => l.ReceivedAtUtc >= todayStart),
                LastReceivedAtUtc = p.Logs.Max(l => (DateTime?)l.ReceivedAtUtc),
            })
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return projects;
    }

    public async Task<ProjectDto?> GetProjectAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var todayStart = DateTime.UtcNow.Date;

        return await _db.Projects
            .Where(p => p.Id == projectId && p.OwnerId == ownerId)
            .Select(p => new ProjectDto
            {
                Id = p.Id,
                Name = p.Name,
                Slug = p.Slug,
                Description = p.Description,
                Environment = p.Environment,
                CreatedAtUtc = p.CreatedAtUtc,
                RetentionMaxAgeDays = p.RetentionMaxAgeDays,
                RetentionMaxEvents = p.RetentionMaxEvents,
                ActiveApiKeyCount = p.ApiKeys.Count(k => k.RevokedAtUtc == null),
                TodayEventCount = p.Logs.Count(l => l.ReceivedAtUtc >= todayStart),
                LastReceivedAtUtc = p.Logs.Max(l => (DateTime?)l.ReceivedAtUtc),
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ProjectDto> UpdateProjectAsync(
        Guid projectId,
        Guid ownerId,
        UpdateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await _db.Projects
            .Include(p => p.ApiKeys)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == ownerId, cancellationToken);

        if (project is null)
        {
            throw new KeyNotFoundException($"Project '{projectId}' not found or not owned by the caller.");
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            project.Name = request.Name.Trim();
        }

        if (request.Description is not null)
        {
            project.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Environment))
        {
            project.Environment = request.Environment.Trim();
        }

        if (request.RetentionMaxAgeDays is not null)
        {
            project.RetentionMaxAgeDays = request.RetentionMaxAgeDays.Value;
        }

        if (request.RetentionMaxEvents is not null)
        {
            project.RetentionMaxEvents = request.RetentionMaxEvents.Value;
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new ProjectDto
        {
            Id = project.Id,
            Name = project.Name,
            Slug = project.Slug,
            Description = project.Description,
            Environment = project.Environment,
            CreatedAtUtc = project.CreatedAtUtc,
            RetentionMaxAgeDays = project.RetentionMaxAgeDays,
            RetentionMaxEvents = project.RetentionMaxEvents,
            ActiveApiKeyCount = project.ApiKeys.Count(k => k.RevokedAtUtc == null),
        };
    }

    public async Task DeleteProjectAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken = default)
    {
        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == ownerId, cancellationToken);

        if (project is null)
        {
            throw new KeyNotFoundException($"Project '{projectId}' not found or not owned by the caller.");
        }

        _keyCache.Remove(projectId);
        _db.Projects.Remove(project);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9]+", "-");
        slug = slug.Trim('-');
        return string.IsNullOrEmpty(slug) ? "project" : slug;
    }

    private async Task<string> GenerateUniqueSlugAsync(string name, CancellationToken cancellationToken)
    {
        var baseSlug = GenerateSlug(name);
        var slug = baseSlug;
        var counter = 1;

        while (await _db.Projects.AnyAsync(p => p.Slug == slug, cancellationToken))
        {
            counter++;
            slug = $"{baseSlug}-{counter}";
        }

        return slug;
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
