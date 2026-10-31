using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arovyx.SealedTrace.Core.Dtos.Ingest;
using Arovyx.SealedTrace.Core.Entities;
using Arovyx.SealedTrace.Core.Interfaces;
using Arovyx.SealedTrace.Infrastructure.Caching;
using Arovyx.SealedTrace.Infrastructure.Crypto;
using Arovyx.SealedTrace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arovyx.SealedTrace.Infrastructure.Services;

public sealed class IngestService : IIngestService
{
    private const int MaxEnvelopes = 50;
    private const long MaxBodySizeBytes = 2_097_152; // 2 MB
    private const int MaxFutureToleranceMinutes = 5;

    private readonly SealedTraceDbContext _db;
    private readonly IEnvelopeDecryptor _decryptor;
    private readonly ProjectKeyCache _keyCache;
    private readonly IProjectKeyProtector _keyProtector;
    private readonly MasterKeyRing _masterKeyRing;

    public IngestService(
        SealedTraceDbContext db,
        IEnvelopeDecryptor decryptor,
        ProjectKeyCache keyCache,
        IProjectKeyProtector keyProtector,
        MasterKeyRing masterKeyRing)
    {
        _db = db;
        _decryptor = decryptor;
        _keyCache = keyCache;
        _keyProtector = keyProtector;
        _masterKeyRing = masterKeyRing;
    }

    public async Task<IngestResponse> IngestAsync(
        Guid projectId,
        IngestRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new IngestResponse();

        if (request.Envelopes == null || request.Envelopes.Count == 0)
        {
            return response;
        }

        if (request.Envelopes.Count > MaxEnvelopes)
        {
            throw new EnvelopeLimitExceededException(MaxEnvelopes);
        }

        // Load the project and its encrypted private key.
        var project = await _db.Projects
            .Include(p => p.ProjectKey)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project is null || project.ProjectKey is null)
        {
            throw new ProjectNotFoundException(projectId);
        }

        // Get the decrypted private key from cache or database.
        var privateKeyPem = _keyCache.Get(projectId);
        if (privateKeyPem is null)
        {
            privateKeyPem = _keyProtector.DecryptPrivateKey(
                project.ProjectKey.PrivateKeyEncrypted,
                project.ProjectKey.MasterKeyVersion);
            _keyCache.Set(projectId, privateKeyPem);
        }

        var accepted = new List<string>();
        var duplicate = new List<string>();
        var rejected = new List<IngestRejection>();

        var now = DateTime.UtcNow;
        var futureCutoff = now.AddMinutes(MaxFutureToleranceMinutes);

        var batchIdsToProcess = new HashSet<string>();

        foreach (var envelope in request.Envelopes)
        {
            // Validate projectId matches the API key's project.
            if (envelope.ProjectId != projectId)
            {
                rejected.Add(new IngestRejection
                {
                    BatchId = envelope.BatchId.ToString(),
                    Reason = "project_mismatch",
                });
                continue;
            }

            // Batch dedupe: check if this batch ID already exists.
            var existingBatch = await _db.IngestBatches
                .FirstOrDefaultAsync(b => b.BatchId == envelope.BatchId, cancellationToken);

            if (existingBatch is not null)
            {
                if (existingBatch.ProjectId == projectId)
                {
                    duplicate.Add(envelope.BatchId.ToString());
                }
                else
                {
                    rejected.Add(new IngestRejection
                    {
                        BatchId = envelope.BatchId.ToString(),
                        Reason = "batch_id_conflict",
                    });
                }
                continue;
            }

            // Prevent duplicate batch IDs within the same request.
            var batchIdKey = envelope.BatchId.ToString();
            if (!batchIdsToProcess.Add(batchIdKey))
            {
                duplicate.Add(envelope.BatchId.ToString());
                continue;
            }

            try
            {
                var logEvents = await _decryptor.DecryptAsync(envelope, privateKeyPem, cancellationToken);

                // Validate event count matches decrypted array length.
                if (logEvents.Count != envelope.EventCount)
                {
                    rejected.Add(new IngestRejection
                    {
                        BatchId = envelope.BatchId.ToString(),
                        Reason = "event_count_mismatch",
                    });
                    continue;
                }

                // Validate each LogEvent and check for duplicate log IDs.
                var logsToInsert = new List<LogEntry>();
                var batchRejected = false;

                foreach (var evt in logEvents)
                {
                    // Check for duplicate LogEntry.Id.
                    var existingLog = await _db.Logs
                        .AnyAsync(l => l.Id == evt.Id, cancellationToken);

                    if (existingLog)
                    {
                        rejected.Add(new IngestRejection
                        {
                            BatchId = envelope.BatchId.ToString(),
                            Reason = "duplicate_log_id",
                        });
                        batchRejected = true;
                        break;
                    }

                    // Validate level enum.
                    if (!TryParseLevel(evt.Level, out var level))
                    {
                        rejected.Add(new IngestRejection
                        {
                            BatchId = envelope.BatchId.ToString(),
                            Reason = "invalid_event",
                        });
                        batchRejected = true;
                        break;
                    }

                    // Reject future timestamps > now + 5 min.
                    if (evt.Ts > futureCutoff)
                    {
                        rejected.Add(new IngestRejection
                        {
                            BatchId = envelope.BatchId.ToString(),
                            Reason = "timestamp_in_future",
                        });
                        batchRejected = true;
                        break;
                    }

                    // Build MetadataJson: metadata + _ctx.
                    var metadataJson = BuildMetadataJson(evt.Metadata, evt.Ctx);

                    logsToInsert.Add(new LogEntry
                    {
                        Id = evt.Id,
                        ProjectId = projectId,
                        BatchId = envelope.BatchId,
                        ClientTs = evt.Ts,
                        ReceivedAtUtc = now,
                        Level = (SealedTraceLevel)level,
                        Tag = evt.Tag,
                        Message = evt.Message,
                        Error = evt.Error,
                        StackTrace = evt.StackTrace,
                        MetadataJson = metadataJson,
                        SessionId = evt.SessionId,
                        UserId = evt.UserId,
                        Seq = evt.Seq,
                        AppVersion = evt.Ctx?.AppVersion,
                        Platform = evt.Ctx?.Platform,
                    });
                }

                if (batchRejected)
                {
                    continue;
                }

                // Persist IngestBatch + Logs in one transaction.
                var eventCount = logEvents.Count;
                var envelopeSize = Encoding.UTF8.GetBytes(
                    JsonSerializer.Serialize(envelope)).Length;

                using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

                var ingestBatch = new IngestBatch
                {
                    BatchId = envelope.BatchId,
                    ProjectId = projectId,
                    ReceivedAtUtc = now,
                    EventCount = logEvents.Count,
                    EnvelopeSize = envelopeSize,
                };

                _db.IngestBatches.Add(ingestBatch);
                _db.Logs.AddRange(logsToInsert);
                await _db.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);

                accepted.Add(envelope.BatchId.ToString());
            }
            catch (InvalidAlgorithmException)
            {
                rejected.Add(new IngestRejection
                {
                    BatchId = envelope.BatchId.ToString(),
                    Reason = "invalid_algorithm",
                });
            }
            catch (InvalidPayloadException)
            {
                rejected.Add(new IngestRejection
                {
                    BatchId = envelope.BatchId.ToString(),
                    Reason = "invalid_payload",
                });
            }
            catch (CryptographicException)
            {
                rejected.Add(new IngestRejection
                {
                    BatchId = envelope.BatchId.ToString(),
                    Reason = "decrypt_failed",
                });
            }
            catch (InvalidEnvelopeException)
            {
                rejected.Add(new IngestRejection
                {
                    BatchId = envelope.BatchId.ToString(),
                    Reason = "invalid_envelope",
                });
            }
        }

        response.Accepted = accepted;
        response.Duplicate = duplicate;
        response.Rejected = rejected;

        return response;
    }

    private static string BuildMetadataJson(
        Dictionary<string, object>? metadata,
        Ctx? ctx)
    {
        var combined = new Dictionary<string, object>();

        if (metadata is not null)
        {
            foreach (var kvp in metadata)
            {
                combined[kvp.Key] = kvp.Value;
            }
        }

        if (ctx is not null)
        {
            var ctxDict = new Dictionary<string, object?>();
            if (ctx.AppVersion is not null) ctxDict["appVersion"] = ctx.AppVersion;
            if (ctx.BuildNumber is not null) ctxDict["buildNumber"] = ctx.BuildNumber;
            if (ctx.Platform is not null) ctxDict["platform"] = ctx.Platform;
            if (ctx.Os is not null) ctxDict["os"] = ctx.Os;
            if (ctx.Locale is not null) ctxDict["locale"] = ctx.Locale;
            if (ctx.Env is not null) ctxDict["env"] = ctx.Env;
            if (ctx.Network is not null) ctxDict["network"] = ctx.Network;

            if (ctxDict.Count > 0)
            {
                combined["_ctx"] = ctxDict;
            }
        }

        return JsonSerializer.Serialize(combined);
    }

    private static bool TryParseLevel(string level, out byte result)
    {
        result = level.ToLowerInvariant() switch
        {
            "verbose" => 0,
            "debug" => 1,
            "info" => 2,
            "warning" => 3,
            "error" => 4,
            "fatal" => 5,
            _ => 255,
        };
        return result != 255;
    }
}

public sealed class EnvelopeLimitExceededException : Exception
{
    public EnvelopeLimitExceededException(int max) : base($"Maximum of {max} envelopes per batch.") { }
}

public sealed class ProjectNotFoundException : Exception
{
    public ProjectNotFoundException(Guid projectId) : base($"Project '{projectId}' not found.") { }
}
