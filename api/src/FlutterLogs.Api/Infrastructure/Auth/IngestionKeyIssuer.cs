using FlutterLogs.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FlutterLogs.Api.Infrastructure.Auth;

/// <summary>
/// Allocates a fresh ingestion key with a prefix that is not already indexed.
/// Shared by project creation (first key) and rotation so both follow one path.
/// </summary>
public sealed class IngestionKeyIssuer(AppDbContext db, IOptions<IngestOptions> options)
{
    public async Task<IssuedIngestionKey> IssueAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = IngestionKeyFactory.Create(options.Value);

            var prefixTaken = await db.ProjectKeys
                .AnyAsync(key => key.Prefix == candidate.Prefix, cancellationToken);

            if (!prefixTaken)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique ingestion-key prefix.");
    }
}
