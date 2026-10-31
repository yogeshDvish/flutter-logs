using System.Diagnostics.Metrics;
using Arovyx.SealedTrace.Core.Entities;
using Arovyx.SealedTrace.Core.Interfaces;
using Arovyx.SealedTrace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arovyx.SealedTrace.Infrastructure.Services;

public sealed class RetentionOptions
{
    public const string SectionName = "Retention";
    public int DefaultMaxAgeDays { get; set; } = 30;
    public long DefaultMaxEvents { get; set; } = 1_000_000;
}

public sealed class RetentionService : IRetentionService
{
    private const int BatchSize = 10_000;
    private const int MaxLoopsPerRun = 100;

    private readonly SealedTraceDbContext _db;
    private readonly RetentionOptions _options;
    private static readonly Meter Meter = new("SealedTrace.Retention");
    private static readonly Counter<long> DeletedLogsCounter = Meter.CreateCounter<long>("sealedtrace.retention.deleted.logs");
    private static readonly Counter<long> DeletedBatchesCounter = Meter.CreateCounter<long>("sealedtrace.retention.deleted.batches");

    public RetentionService(SealedTraceDbContext db, IOptions<RetentionOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task RunRetentionAsync(CancellationToken cancellationToken = default)
    {
        var projects = await _db.Projects.ToListAsync(cancellationToken);

        foreach (var project in projects)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var maxAgeDays = project.RetentionMaxAgeDays ?? _options.DefaultMaxAgeDays;
            var maxEvents = project.RetentionMaxEvents ?? _options.DefaultMaxEvents;

            // Age-based deletion.
            var cutoffUtc = DateTime.UtcNow.AddDays(-maxAgeDays);
            var ageDeleted = await DeleteByAgeAsync(project.Id, cutoffUtc, cancellationToken);
            DeletedLogsCounter.Add(ageDeleted);

            // Event-count-based deletion (delete oldest beyond maxEvents).
            var countDeleted = await DeleteExcessEventsAsync(project.Id, maxEvents, cancellationToken);
            DeletedLogsCounter.Add(countDeleted);

            // Delete orphaned IngestBatches (batches with no logs).
            var orphanDeleted = await DeleteOrphanedBatchesAsync(project.Id, cancellationToken);
            DeletedBatchesCounter.Add(orphanDeleted);
        }

        // Purge expired/revoked refresh tokens.
        var purgeDeleted = await PurgeRefreshTokensAsync(cancellationToken);
        DeletedLogsCounter.Add(purgeDeleted);
    }

    private async Task<long> DeleteByAgeAsync(
        Guid projectId,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        var totalDeleted = 0L;

        for (var i = 0; i < MaxLoopsPerRun; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var deleted = await _db.Database.ExecuteSqlRawAsync(
                @"DELETE TOP (@batchSize) FROM dbo.Logs
                  WHERE ProjectId = @projectId AND ClientTs < @cutoffUtc",
                new Microsoft.Data.SqlClient.SqlParameter("@batchSize", BatchSize),
                new Microsoft.Data.SqlClient.SqlParameter("@projectId", projectId),
                new Microsoft.Data.SqlClient.SqlParameter("@cutoffUtc", cutoffUtc),
                cancellationToken);

            totalDeleted += deleted;
            if (deleted < BatchSize)
                break;
        }

        return totalDeleted;
    }

    private async Task<long> DeleteExcessEventsAsync(
        Guid projectId,
        long maxEvents,
        CancellationToken cancellationToken)
    {
        var totalDeleted = 0L;

        var currentCount = await _db.Logs
            .Where(l => l.ProjectId == projectId)
            .LongCountAsync(cancellationToken);

        if (currentCount <= maxEvents)
            return 0;

        var excess = currentCount - maxEvents;

        for (var i = 0; i < MaxLoopsPerRun; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var deleted = await _db.Database.ExecuteSqlRawAsync(
                @"DELETE TOP (@batchSize) FROM dbo.Logs
                  WHERE Id IN (
                      SELECT TOP (@batchSize) Id FROM dbo.Logs
                      WHERE ProjectId = @projectId
                      ORDER BY ClientTs ASC, Id ASC
                  )",
                new Microsoft.Data.SqlClient.SqlParameter("@batchSize", BatchSize),
                new Microsoft.Data.SqlClient.SqlParameter("@projectId", projectId),
                cancellationToken);

            totalDeleted += deleted;
            if (deleted < BatchSize)
                break;

            var remaining = await _db.Logs
                .Where(l => l.ProjectId == projectId)
                .LongCountAsync(cancellationToken);

            if (remaining <= maxEvents)
                break;
        }

        return totalDeleted;
    }

    private async Task<long> DeleteOrphanedBatchesAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var deleted = await _db.Database.ExecuteSqlRawAsync(
            @"DELETE b FROM dbo.IngestBatches b
              LEFT JOIN dbo.Logs l ON l.BatchId = b.BatchId
              WHERE b.ProjectId = @projectId AND l.Id IS NULL",
            new Microsoft.Data.SqlClient.SqlParameter("@projectId", projectId),
            cancellationToken);

        return deleted;
    }

    private async Task<long> PurgeRefreshTokensAsync(CancellationToken cancellationToken)
    {
        var tokens = await _db.RefreshTokens
            .Where(rt => rt.ExpiresAtUtc <= DateTime.UtcNow || rt.RevokedAtUtc != null)
            .ToListAsync(cancellationToken);

        _db.RefreshTokens.RemoveRange(tokens);
        return await _db.SaveChangesAsync(cancellationToken);
    }
}

public interface IRetentionService
{
    Task RunRetentionAsync(CancellationToken cancellationToken = default);
}
