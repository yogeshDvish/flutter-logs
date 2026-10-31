namespace Arovyx.SealedTrace.Core.Dtos.Logs;

public sealed class LogQueryRequest
{
    public List<string> Level { get; set; } = new();
    public string? Tag { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string? UserId { get; set; }
    public Guid? SessionId { get; set; }
    public string? Search { get; set; }
    public string? Cursor { get; set; }
    public int Limit { get; set; } = 50;
}

public sealed class LogEntryDto
{
    public Guid Id { get; set; }
    public int Seq { get; set; }
    public DateTime Ts { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
    public string? StackTrace { get; set; }
    public string? MetadataJson { get; set; }
    public Guid? SessionId { get; set; }
    public string? UserId { get; set; }
    public string? AppVersion { get; set; }
    public string? Platform { get; set; }
}

public sealed class LogQueryResponse
{
    public List<LogEntryDto> Items { get; set; } = new();
    public string? NextCursor { get; set; }
}
