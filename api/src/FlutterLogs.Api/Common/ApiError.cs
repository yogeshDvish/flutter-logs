namespace FlutterLogs.Api.Common;

/// <summary>An expected, machine-readable failure. Never carries secret material.</summary>
public sealed record ApiError(
    string Code,
    string Message,
    int StatusCode,
    IReadOnlyDictionary<string, string[]>? Errors = null);

public sealed class OperationResult<T>
{
    public OperationResult(T? value, ApiError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public ApiError? Error { get; }

    public bool IsSuccess => Error is null;
}

public static class OperationResults
{
    public static OperationResult<T> Ok<T>(T value) => new(value, null);

    public static OperationResult<T> Fail<T>(ApiError error) => new(default, error);
}
