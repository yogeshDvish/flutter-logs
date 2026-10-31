using Arovyx.SealedTrace.Core.Dtos.Auth;
using Arovyx.SealedTrace.Core.Entities;
using Arovyx.SealedTrace.Core.Interfaces;
using Arovyx.SealedTrace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Arovyx.SealedTrace.Infrastructure.Auth;

public sealed class AuthService : IAuthService
{
    private readonly SealedTraceDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly RefreshTokenService _refreshTokens;

    public AuthService(
        SealedTraceDbContext db,
        JwtTokenService jwt,
        RefreshTokenService refreshTokens)
    {
        _db = db;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
    }

    public async Task<AuthUserDto> RegisterAsync(
        AuthRegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var emailTaken = await _db.Users
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (emailTaken)
        {
            throw new EmailTakenException(normalizedEmail);
        }

        var user = new User
        {
            Email = normalizedEmail,
            PasswordHash = PasswordHasher.Hash(request.Password),
            Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        return new AuthUserDto
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
        };
    }

    public async Task<AuthLoginResponse> LoginAsync(
        AuthLoginRequest request,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new InvalidCredentialsException();
        }

        var accessToken = _jwt.CreateAccessToken(user);

        var (refreshTokenPlaintext, _) = await _refreshTokens.CreateAsync(
            user.Id,
            DateTime.UtcNow.AddDays(30),
            cancellationToken);

        return new AuthLoginResponse
        {
            AccessToken = accessToken,
            ExpiresIn = 900,
            RefreshTokenPlaintext = refreshTokenPlaintext,
            User = new AuthUserDto
            {
                Id = user.Id,
                Email = user.Email,
                Name = user.Name,
            },
        };
    }

    public async Task<AuthLoginResponse> RefreshAsync(
        string refreshToken,
        string ipAddress,
        CancellationToken cancellationToken = default)
    {
        var result = await _refreshTokens.ValidateAndRotateAsync(refreshToken, cancellationToken);

        if (!result.Success)
        {
            throw new InvalidRefreshTokenException();
        }

        var user = await _db.Users.FindAsync(new object?[] { result.UserId }, cancellationToken);
        if (user is null)
        {
            throw new InvalidRefreshTokenException();
        }

        var accessToken = _jwt.CreateAccessToken(user);

        return new AuthLoginResponse
        {
            AccessToken = accessToken,
            ExpiresIn = 900,
            RefreshTokenPlaintext = result.NewTokenPlaintext,
            User = new AuthUserDto
            {
                Id = user.Id,
                Email = user.Email,
                Name = user.Name,
            },
        };
    }

    public async Task LogoutAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        await _refreshTokens.RevokeAsync(refreshToken, cancellationToken);
    }

    public async Task<AuthMeResponse> MeAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .Select(u => new AuthMeResponse
            {
                Id = u.Id,
                Email = u.Email,
                Name = u.Name,
            })
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            throw new UserNotFoundException(userId);
        }

        return user;
    }
}

public sealed class EmailTakenException : Exception
{
    public EmailTakenException(string email) : base($"Email '{email}' is already taken.") { }
}

public sealed class InvalidCredentialsException : Exception { }

public sealed class InvalidRefreshTokenException : Exception { }

public sealed class UserNotFoundException : Exception
{
    public UserNotFoundException(Guid userId) : base($"User '{userId}' not found.") { }
}
