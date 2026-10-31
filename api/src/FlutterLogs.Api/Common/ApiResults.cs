namespace FlutterLogs.Api.Common;

/// <summary>Builds RFC 7807 responses that carry the stable <c>code</c> extension.</summary>
public static class ApiResults
{
    public static IResult Problem(ApiError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var extensions = new Dictionary<string, object?>
        {
            ["code"] = error.Code,
        };

        if (error.Errors is not null)
        {
            extensions["errors"] = error.Errors;
        }

        return Results.Problem(
            statusCode: error.StatusCode,
            title: error.Message,
            extensions: extensions);
    }

    public static IResult ValidationProblem(IReadOnlyDictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "One or more validation errors occurred.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "validation_failed",
                ["errors"] = errors,
            });
    }

    public static IResult Unauthorized() =>
        Problem(new ApiError(
            "unauthorized",
            "Authentication is required.",
            StatusCodes.Status401Unauthorized));
}
