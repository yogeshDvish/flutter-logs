namespace FlutterLogs.Api.Features.Keys;

public sealed record CreateProjectKeyRequest(string? Label, int? GraceHours);

public sealed record ProjectKeyDto(
    Guid Id,
    string Prefix,
    string? Label,
    string Status,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    DateTime? GraceUntil,
    DateTime? RevokedAt);

/// <summary>
/// The only response that contains <c>Plaintext</c>. It is never returned by the
/// list endpoint and cannot be recovered later.
/// </summary>
public sealed record IssuedKeyResponse(
    Guid Id,
    string Prefix,
    string? Label,
    DateTime CreatedAt,
    DateTime? GraceUntil,
    string Plaintext);
