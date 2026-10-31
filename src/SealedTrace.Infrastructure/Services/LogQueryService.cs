using System.Globalization;
using System.Text;
using Arovyx.SealedTrace.Core.Dtos.Logs;
using Arovyx.SealedTrace.Core.Entities;
using Arovyx.SealedTrace.Core.Interfaces;
using Arovyx.SealedTrace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arovyx.SealedTrace.Infrastructure.Services;

public sealed class LogQueryService : ILogQueryService
{
    private const int MaxLimit = 200;
    private const int DefaultLimit = 50;
    private const int MaxRangeDays = 90;

    private readonly SealedTraceDbContext _db;

    public LogQueryService(SealedTraceDbContext db)
    {
        _db = db;
    }

    public async Task<LogQueryResponse> QueryLogsAsync(
        Guid projectId,
        Guid ownerId,
        LogQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        // Verify project ownership.
        var project = await _db.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == ownerId, cancellationToken);

        if (project is null)
        {
            throw new ProjectNotFoundException(projectId);
        }

        // Validate limit.
        var limit = request.Limit > 0 ? request.Limit : DefaultLimit;
        if (limit > MaxLimit)
        {
            limit = MaxLimit;
        }

        // Validate range.
        var from = request.From;
        var to = request.To;

        if (from.HasValue && to.HasValue && to.Value <= from.Value)
        {
            throw new InvalidRangeException("'to' must be after 'from'.");
        }

        if (from.HasValue && to.HasValue && (to.Value - from.Value).TotalDays > MaxRangeDays)
        {
            throw new InvalidRangeException($"Range cannot exceed {MaxRangeDays} days.");
        }

        // Default range: last 24 hours when omitted.
        if (!from.HasValue && !to.HasValue)
        {
            from = DateTime.UtcNow.AddDays(-1);
            to = DateTime.UtcNow;
        }

        // Decode cursor.
        DateTime? cursorTs = null;
        Guid? cursorId = null;
        if (!string.IsNullOrWhiteSpace(request.Cursor))
        {
            var decoded = DecodeCursor(request.Cursor);
            cursorTs = decoded.ts;
            cursorId = decoded.id;
        }

        // Build query.
        var query = _db.Logs
            .Where(l => l.ProjectId == projectId)
            .Where(l => l.ClientTs >= from!.Value)
            .Where(l => l.ClientTs < to!.Value);

        // Keyset pagination filter.
        if (cursorTs.HasValue && cursorId.HasValue)
        {
            query = query.Where(l =>
                l.ClientTs < cursorTs.Value ||
                (l.ClientTs == cursorTs.Value && l.Id < cursorId.Value));
        }

        // Filters.
        if (request.Level != null && request.Level.Count > 0)
        {
            var levelBytes = request.Level
                .Select(l =>
                {
                    return l.ToLowerInvariant() switch
                    {
                        "verbose" => (byte)0,
                        "debug" => (byte)1,
                        "info" => (byte)2,
                        "warning" => (byte)3,
                        "error" => (byte)4,
                        "fatal" => (byte)5,
                        _ => (byte)255,
                    };
                })
                .Where(b => b != 255)
                .ToList();

            if (levelBytes.Count > 0)
            {
                query = query.Where(l => levelBytes.Contains((byte)l.Level));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Tag))
        {
            query = query.Where(l => l.Tag == request.Tag);
        }

        if (!string.IsNullOrWhiteSpace(request.UserId))
        {
            query = query.Where(l => l.UserId == request.UserId);
        }

        if (request.SessionId.HasValue)
        {
            query = query.Where(l => l.SessionId == request.SessionId.Value);
        }

        // Search: full-text CONTAINS when enabled, LIKE fallback otherwise.
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var useFullText = _db.Database.GetDbConnection().DataSource.Contains("fulltext") ||
                              _db.Database.GetDbConnection().ConnectionString.Contains("fulltext");
            // Full-text is configured via appsetting; fall back to LIKE for now.
            query = query.Where(l => EF.Functions.Like(l.Message, $"%{request.Search}%"));
        }

        // Order by keyset and take limit + 1 for next cursor detection.
        var rawResults = await query
            .OrderByDescending(l => l.ClientTs)
            .ThenByDescending(l => l.Id)
            .Take(limit + 1)
            .Select(l => new
            {
                l.Id,
                l.Seq,
                l.ClientTs,
                l.ReceivedAtUtc,
                LevelByte = l.Level,
                l.Tag,
                l.Message,
                l.Error,
                l.StackTrace,
                l.MetadataJson,
                l.SessionId,
                l.UserId,
                l.AppVersion,
                l.Platform,
            })
            .ToListAsync(cancellationToken);

        var results = rawResults.Select(r => new LogEntryDto
        {
            Id = r.Id,
            Seq = (int)r.Seq,
            Ts = r.ClientTs,
            ReceivedAtUtc = r.ReceivedAtUtc,
            Level = ((SealedTraceLevel)r.LevelByte).ToString().ToLowerInvariant(),
            Tag = r.Tag,
            Message = r.Message,
            Error = r.Error,
            StackTrace = r.StackTrace,
            MetadataJson = r.MetadataJson,
            SessionId = r.SessionId,
            UserId = r.UserId,
            AppVersion = r.AppVersion,
            Platform = r.Platform,
        }).ToList();

        var items = results.Take(limit).ToList();
        string? nextCursor = null;

        if (results.Count > limit)
        {
            var last = items[^1];
            nextCursor = EncodeCursor(last.Ts, last.Id);
        }

        return new LogQueryResponse
        {
            Items = items,
            NextCursor = nextCursor,
        };
    }

    private static string EncodeCursor(DateTime ts, Guid id)
    {
        var raw = $"{ts:yyyy-MM-ddTHH:mm:ss.fffZ}|{id}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private static (DateTime ts, Guid id) DecodeCursor(string cursor)
    {
        var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        var parts = raw.Split('|');
        var ts = DateTime.Parse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (ts.Kind != DateTimeKind.Utc)
            ts = DateTime.SpecifyKind(ts, DateTimeKind.Utc);
        return (ts, Guid.Parse(parts[1]));
    }
}

public sealed class InvalidRangeException : Exception
{
    public InvalidRangeException(string message) : base(message) { }
}
