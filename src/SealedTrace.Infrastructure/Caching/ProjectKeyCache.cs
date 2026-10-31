using Microsoft.Extensions.Caching.Memory;

namespace Arovyx.SealedTrace.Infrastructure.Caching;

/// <summary>
/// In-memory cache for decrypted project RSA private keys.
/// Bounded size with absolute expiration; never logged.
/// </summary>
public sealed class ProjectKeyCache
{
    private const int CacheSizeLimit = 1000;
    private static readonly TimeSpan AbsoluteExpiration = TimeSpan.FromMinutes(30);

    private readonly IMemoryCache _cache;

    public ProjectKeyCache(IMemoryCache memoryCache)
    {
        _cache = memoryCache;
    }

    public string? Get(Guid projectId)
    {
        return _cache.TryGetValue(projectId, out string? pem) ? pem : null;
    }

    public void Set(Guid projectId, string pem)
    {
        _cache.Set(projectId, pem, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = AbsoluteExpiration,
        });
    }

    public void Remove(Guid projectId)
    {
        _cache.Remove(projectId);
    }
}
