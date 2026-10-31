using Arovyx.SealedTrace.Core.Dtos.Auth;

namespace Arovyx.SealedTrace.Core.Interfaces;

public interface IAuthService
{
    Task<AuthUserDto> RegisterAsync(AuthRegisterRequest request, CancellationToken cancellationToken = default);

    Task<AuthLoginResponse> LoginAsync(AuthLoginRequest request, string ipAddress, CancellationToken cancellationToken = default);

    Task<AuthLoginResponse> RefreshAsync(string refreshToken, string ipAddress, CancellationToken cancellationToken = default);

    Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<AuthMeResponse> MeAsync(Guid userId, CancellationToken cancellationToken = default);
}
