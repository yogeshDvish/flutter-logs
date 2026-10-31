using Arovyx.SealedTrace.Core.Dtos.Stats;

namespace Arovyx.SealedTrace.Core.Interfaces;

public interface IStatsService
{
    Task<StatsResponse> GetStatsAsync(
        Guid projectId,
        Guid ownerId,
        StatsQueryRequest request,
        CancellationToken cancellationToken = default);
}
