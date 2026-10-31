using FlutterLogs.Api.Common;
using FlutterLogs.Api.Infrastructure.Auth;
using FlutterLogs.Api.Infrastructure.Email;
using FlutterLogs.Api.Infrastructure.Persistence;
using FlutterLogs.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FlutterLogs.Api.Features.Auth;

public sealed partial class AuthService(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    IEmailSender emailSender,
    IOptions<AuthOptions> authOptions,
    TimeProvider timeProvider,
    ILogger<AuthService> logger)
{
    // Equalises login timing for unknown emails without storing a real account.
    private static string? dummyHash;

    private AuthOptions Options => authOptions.Value;

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<OperationResult<UserDto>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);

        if (await db.Users.AnyAsync(user => user.Email == email, cancellationToken))
        {
            return Failure<UserDto>(
                StatusCodes.Status409Conflict,
                "email_taken",
                "An account with that email already exists.");
        }

        var now = Now;

        var user = new User
        {
            Email = email,
            PasswordHash = await passwordHasher.HashAsync(request.Password, cancellationToken),
            DisplayName = NormalizeDisplayName(request.DisplayName),
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The only unique constraint on users is the email address.
            return Failure<UserDto>(
                StatusCodes.Status409Conflict,
                "email_taken",
                "An account with that email already exists.");
        }

        await IssueEmailTokenAsync(
            user,
            EmailTokenPurpose.VerifyEmail,
            TimeSpan.FromHours(Options.EmailVerificationHours),
            cancellationToken);

        LogAccountRegistered(logger, user.Id);

        return OperationResults.Ok(UserDto.FromEntity(user));
    }

    public async Task<OperationResult<AuthSession>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(
            candidate => candidate.Email == email,
            cancellationToken);

        if (user is null)
        {
            await VerifyDummyAsync(request.Password, cancellationToken);
            return InvalidCredentials();
        }

        if (!await passwordHasher.VerifyAsync(request.Password, user.PasswordHash, cancellationToken))
        {
            return InvalidCredentials();
        }

        if (user.Status == UserStatus.Disabled)
        {
            return Failure<AuthSession>(
                StatusCodes.Status403Forbidden,
                "account_disabled",
                "This account has been disabled.");
        }

        return OperationResults.Ok(await CreateSessionAsync(user, cancellationToken));
    }

    public async Task<OperationResult<AuthSession>> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var now = Now;
        var tokenHash = OpaqueToken.Hash(refreshToken);

        var existing = await db.RefreshTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (existing is null)
        {
            return Failure<AuthSession>(
                StatusCodes.Status401Unauthorized,
                "invalid_refresh_token",
                "The refresh token is invalid.");
        }

        if (existing.RevokedAt is not null)
        {
            await RevokeFamilyAsync(existing.FamilyId, now, cancellationToken);

            LogRefreshReuse(logger, existing.UserId, existing.FamilyId);

            return Failure<AuthSession>(
                StatusCodes.Status401Unauthorized,
                "token_reuse_detected",
                "The session has been revoked. Sign in again.");
        }

        if (existing.ExpiresAt <= now)
        {
            return Failure<AuthSession>(
                StatusCodes.Status401Unauthorized,
                "refresh_token_expired",
                "The session has expired. Sign in again.");
        }

        if (existing.User is null || existing.User.Status == UserStatus.Disabled)
        {
            return Failure<AuthSession>(
                StatusCodes.Status401Unauthorized,
                "invalid_refresh_token",
                "The refresh token is invalid.");
        }

        var replacementRaw = OpaqueToken.Create();
        var replacement = new RefreshToken
        {
            UserId = existing.UserId,
            FamilyId = existing.FamilyId,
            TokenHash = OpaqueToken.Hash(replacementRaw),
            CreatedAt = now,
            ExpiresAt = now.AddDays(Options.RefreshTokenDays),
        };

        existing.RevokedAt = now;
        existing.ReplacedById = replacement.Id;
        db.RefreshTokens.Add(replacement);

        await db.SaveChangesAsync(cancellationToken);

        var accessToken = jwtTokenService.CreateAccessToken(existing.User);

        return OperationResults.Ok(new AuthSession(
            accessToken.Token,
            SecondsUntil(accessToken.ExpiresAt, now),
            replacementRaw,
            replacement.ExpiresAt,
            UserDto.FromEntity(existing.User)));
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var tokenHash = OpaqueToken.Hash(refreshToken);

        var existing = await db.RefreshTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (existing is null)
        {
            return;
        }

        await RevokeFamilyAsync(existing.FamilyId, Now, cancellationToken);
    }

    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);

    public async Task<OperationResult<bool>> VerifyEmailAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var now = Now;

        var record = await db.EmailTokens
            .Include(candidate => candidate.User)
            .SingleOrDefaultAsync(
                candidate => candidate.TokenHash == OpaqueToken.Hash(token)
                    && candidate.Purpose == EmailTokenPurpose.VerifyEmail,
                cancellationToken);

        var error = ValidateEmailToken(record, now);
        if (error is not null)
        {
            return OperationResults.Fail<bool>(error);
        }

        record!.UsedAt = now;

        if (record.User is not null && record.User.EmailVerifiedAt is null)
        {
            record.User.EmailVerifiedAt = now;
            record.User.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        return OperationResults.Ok(true);
    }

    public async Task<OperationResult<bool>> ResendVerificationAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(
            candidate => candidate.Id == userId,
            cancellationToken);

        if (user is null)
        {
            return Failure<bool>(
                StatusCodes.Status404NotFound,
                "not_found",
                "Account not found.");
        }

        if (user.EmailVerifiedAt is not null)
        {
            return OperationResults.Ok(true);
        }

        await InvalidateEmailTokensAsync(user.Id, EmailTokenPurpose.VerifyEmail, Now, cancellationToken);
        await IssueEmailTokenAsync(
            user,
            EmailTokenPurpose.VerifyEmail,
            TimeSpan.FromHours(Options.EmailVerificationHours),
            cancellationToken);

        return OperationResults.Ok(true);
    }

    /// <summary>
    /// Always completes without revealing whether the address exists. Callers return
    /// the same response either way.
    /// </summary>
    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = NormalizeEmail(email);

        var user = await db.Users.SingleOrDefaultAsync(
            candidate => candidate.Email == normalized,
            cancellationToken);

        if (user is null)
        {
            return;
        }

        await InvalidateEmailTokensAsync(user.Id, EmailTokenPurpose.ResetPassword, Now, cancellationToken);
        await IssueEmailTokenAsync(
            user,
            EmailTokenPurpose.ResetPassword,
            TimeSpan.FromHours(Options.PasswordResetHours),
            cancellationToken);
    }

    public async Task<OperationResult<bool>> ResetPasswordAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var now = Now;

        var record = await db.EmailTokens
            .Include(candidate => candidate.User)
            .SingleOrDefaultAsync(
                candidate => candidate.TokenHash == OpaqueToken.Hash(request.Token)
                    && candidate.Purpose == EmailTokenPurpose.ResetPassword,
                cancellationToken);

        var error = ValidateEmailToken(record, now);
        if (error is not null)
        {
            return OperationResults.Fail<bool>(error);
        }

        var user = record!.User;

        if (user is null)
        {
            return Failure<bool>(
                StatusCodes.Status400BadRequest,
                "invalid_token",
                "The password reset token is invalid.");
        }

        user.PasswordHash = await passwordHasher.HashAsync(request.NewPassword, cancellationToken);
        user.UpdatedAt = now;
        record.UsedAt = now;

        // A password reset terminates every existing session.
        await db.RefreshTokens
            .Where(token => token.UserId == user.Id && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, now),
                cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        LogPasswordReset(logger, user.Id);

        return OperationResults.Ok(true);
    }

    private async Task<AuthSession> CreateSessionAsync(User user, CancellationToken cancellationToken)
    {
        var now = Now;
        var accessToken = jwtTokenService.CreateAccessToken(user);
        var rawRefreshToken = OpaqueToken.Create();

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            FamilyId = Guid.CreateVersion7(),
            TokenHash = OpaqueToken.Hash(rawRefreshToken),
            CreatedAt = now,
            ExpiresAt = now.AddDays(Options.RefreshTokenDays),
        };

        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync(cancellationToken);

        return new AuthSession(
            accessToken.Token,
            SecondsUntil(accessToken.ExpiresAt, now),
            rawRefreshToken,
            refreshToken.ExpiresAt,
            UserDto.FromEntity(user));
    }

    private async Task IssueEmailTokenAsync(
        User user,
        EmailTokenPurpose purpose,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        var now = Now;
        var token = OpaqueToken.Create();

        db.EmailTokens.Add(new EmailToken
        {
            UserId = user.Id,
            Purpose = purpose,
            TokenHash = OpaqueToken.Hash(token),
            CreatedAt = now,
            ExpiresAt = now.Add(lifetime),
        });

        await db.SaveChangesAsync(cancellationToken);

        var path = purpose == EmailTokenPurpose.VerifyEmail
            ? "/verify-email"
            : "/reset-password";

        var link = $"{Options.PublicBaseUrl.TrimEnd('/')}{path}?token={Uri.EscapeDataString(token)}";

        if (purpose == EmailTokenPurpose.VerifyEmail)
        {
            await emailSender.SendVerificationAsync(user.Email, link, cancellationToken);
        }
        else
        {
            await emailSender.SendPasswordResetAsync(user.Email, link, cancellationToken);
        }
    }

    private async Task InvalidateEmailTokensAsync(
        Guid userId,
        EmailTokenPurpose purpose,
        DateTime now,
        CancellationToken cancellationToken) =>
        await db.EmailTokens
            .Where(token => token.UserId == userId
                && token.Purpose == purpose
                && token.UsedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.UsedAt, now),
                cancellationToken);

    private async Task RevokeFamilyAsync(
        Guid familyId,
        DateTime now,
        CancellationToken cancellationToken) =>
        await db.RefreshTokens
            .Where(token => token.FamilyId == familyId && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, now),
                cancellationToken);

    private async Task VerifyDummyAsync(string password, CancellationToken cancellationToken)
    {
        var hash = Volatile.Read(ref dummyHash);

        if (hash is null)
        {
            hash = await passwordHasher.HashAsync("flutter-logs-dummy-password", cancellationToken);
            Volatile.Write(ref dummyHash, hash);
        }

        await passwordHasher.VerifyAsync(password, hash, cancellationToken);
    }

    private static ApiError? ValidateEmailToken(EmailToken? record, DateTime now)
    {
        if (record is null)
        {
            return new ApiError(
                "invalid_token",
                "The token is invalid.",
                StatusCodes.Status400BadRequest);
        }

        if (record.UsedAt is not null)
        {
            return new ApiError(
                "token_already_used",
                "The token has already been used.",
                StatusCodes.Status400BadRequest);
        }

        if (record.ExpiresAt <= now)
        {
            return new ApiError(
                "token_expired",
                "The token has expired.",
                StatusCodes.Status400BadRequest);
        }

        return null;
    }

    private static OperationResult<AuthSession> InvalidCredentials() =>
        Failure<AuthSession>(
            StatusCodes.Status401Unauthorized,
            "invalid_credentials",
            "Email or password is incorrect.");

    private static OperationResult<T> Failure<T>(int statusCode, string code, string message) =>
        OperationResults.Fail<T>(new ApiError(code, message, statusCode));

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static string? NormalizeDisplayName(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();

    private static int SecondsUntil(DateTime expiresAt, DateTime now) =>
        Math.Max(1, (int)Math.Floor((expiresAt - now).TotalSeconds));

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Account {UserId} registered.")]
    private static partial void LogAccountRegistered(ILogger logger, Guid userId);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "Refresh token reuse detected for account {UserId}; family {FamilyId} revoked.")]
    private static partial void LogRefreshReuse(ILogger logger, Guid userId, Guid familyId);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information, Message = "Password reset completed for account {UserId}; all refresh sessions revoked.")]
    private static partial void LogPasswordReset(ILogger logger, Guid userId);
}
