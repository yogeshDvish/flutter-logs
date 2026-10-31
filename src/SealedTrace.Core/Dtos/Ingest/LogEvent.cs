namespace Arovyx.SealedTrace.Core.Dtos.Ingest;

public sealed class LogEvent
{
    public Guid Id { get; set; }
    public int Seq { get; set; }
    public DateTime Ts { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
    public string? StackTrace { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public Guid? SessionId { get; set; }
    public string? UserId { get; set; }
    public Ctx? Ctx { get; set; }
}

public sealed class Ctx
{
    public string? AppVersion { get; set; }
    public string? BuildNumber { get; set; }
    public string? Platform { get; set; }
    public string? Os { get; set; }
    public string? Locale { get; set; }
    public string? Env { get; set; }
    public string? Network { get; set; }
}
