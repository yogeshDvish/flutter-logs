using Arovyx.SealedTrace.Core.Dtos.Logs;

namespace Arovyx.SealedTrace.Core.Interfaces;

public interface ILogQueryService
{
    Task<LogQueryResponse> QueryLogsAsync(
        Guid projectId,
        Guid ownerId,
        LogQueryRequest request,
        CancellationToken cancellationToken = default);
}
