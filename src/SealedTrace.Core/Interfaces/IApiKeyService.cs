using Arovyx.SealedTrace.Core.Dtos.ApiKeys;

namespace Arovyx.SealedTrace.Core.Interfaces;

public interface IApiKeyService
{
    Task<IReadOnlyList<ApiKeyDto>> ListApiKeysAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken = default);

    Task<ApiKeyCreatedDto> CreateApiKeyAsync(
        Guid projectId,
        Guid ownerId,
        CancellationToken cancellationToken = default);

    Task RevokeApiKeyAsync(
        Guid projectId,
        Guid keyId,
        Guid ownerId,
        CancellationToken cancellationToken = default);
}
