using Arovyx.SealedTrace.Core.Dtos.Ingest;

namespace Arovyx.SealedTrace.Core.Interfaces;

public interface IIngestService
{
    Task<IngestResponse> IngestAsync(
        Guid projectId,
        IngestRequest request,
        CancellationToken cancellationToken = default);
}
