using System.Globalization;
using Arovyx.SealedTrace.Core.Dtos.Stats;
using Arovyx.SealedTrace.Core.Entities;
using Arovyx.SealedTrace.Core.Interfaces;
using Arovyx.SealedTrace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arovyx.SealedTrace.Infrastructure.Services;

public sealed class StatsService : IStatsService
{
    private const int MaxRangeDays = 90;
    private const int DefaultRangeDays = 7;
    private const int TopN = 10;

    private readonly SealedTraceDbContext _db;

    public StatsService(SealedTraceDbContext db)
    {
        _db = db;
    }

    public async Task<StatsResponse> GetStatsAsync(
        Guid projectId,
        Guid ownerId,
        StatsQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        // Verify project ownership.
        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == ownerId, cancellationToken);

        if (project is null)
        {
            throw new ProjectNotFoundException(projectId);
        }

        var to = request.To ?? DateTime.UtcNow;
        var from = request.From ?? to.AddDays(-DefaultRangeDays);

        if (to <= from)
        {
            throw new InvalidRangeException("'to' must be after 'from'.");
        }

        if ((to - from).TotalDays > MaxRangeDays)
        {
            throw new InvalidRangeException($"Range cannot exceed {MaxRangeDays} days.");
        }

        var baseQuery = _db.Logs
            .Where(l => l.ProjectId == projectId)
            .Where(l => l.ClientTs >= from)
            .Where(l => l.ClientTs < to);

        // Materialize logs first, then aggregate in memory to avoid EF translation issues.
        var logs = await baseQuery
            .Select(l => new { l.Level, l.ClientTs, l.Tag, l.UserId })
            .ToListAsync(cancellationToken);

        var totalEvents = (long)logs.Count;

        // Level distribution.
        var levels = logs
            .GroupBy(l => l.Level)
            .Select(g => new StatsDimension
            {
                Name = ((SealedTraceLevel)g.Key).ToString().ToLowerInvariant(),
                Count = (long)g.Count(),
            })
            .OrderByDescending(d => d.Count)
            .ToList();

        // Events per day.
        var eventsPerDay = logs
            .GroupBy(l => l.ClientTs.Date)
            .Select(g => new StatsDimension
            {
                Name = g.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Count = (long)g.Count(),
            })
            .OrderBy(d => d.Name)
            .ToList();

        // Top tags.
        var topTags = logs
            .GroupBy(l => l.Tag)
            .Select(g => new StatsDimension
            {
                Name = g.Key,
                Count = (long)g.Count(),
            })
            .OrderByDescending(d => d.Count)
            .Take(TopN)
            .ToList();

        // Top user IDs (exclude nulls).
        var topUserIds = logs
            .Where(l => l.UserId != null)
            .GroupBy(l => l.UserId!)
            .Select(g => new StatsDimension
            {
                Name = g.Key,
                Count = (long)g.Count(),
            })
            .OrderByDescending(d => d.Count)
            .Take(TopN)
            .ToList();

        return new StatsResponse
        {
            From = from,
            To = to,
            TotalEvents = totalEvents,
            Levels = levels,
            EventsPerDay = eventsPerDay,
            TopTags = topTags,
            TopUserIds = topUserIds,
        };
    }
}
