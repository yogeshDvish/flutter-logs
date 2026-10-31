using Arovyx.SealedTrace.Core.Dtos.Ingest;

namespace Arovyx.SealedTrace.Core.Interfaces;

public interface IEnvelopeDecryptor
{
    Task<List<LogEvent>> DecryptAsync(
        SealedEnvelope envelope,
        string privateKeyPem,
        CancellationToken cancellationToken = default);
}
