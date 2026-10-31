using FluentValidation;

namespace FlutterLogs.Api.Common;

public static class ValidationExtensions
{
    /// <summary>
    /// Runs a validator and returns errors keyed by property name, or null when
    /// valid. Field names are lower-cased by the dashboard before display.
    /// </summary>
    public static Dictionary<string, string[]>? ValidateToDictionary<T>(
        this IValidator<T> validator,
        T request)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var result = validator.Validate(request);

        if (result.IsValid)
        {
            return null;
        }

        return result.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.ErrorMessage).ToArray());
    }
}
