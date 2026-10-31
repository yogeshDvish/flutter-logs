using Arovyx.SealedTrace.Core.Dtos.Logs;
using Arovyx.SealedTrace.Core.Dtos.Stats;
using FluentValidation;

namespace Arovyx.SealedTrace.Api.Validation;

public sealed class LogQueryValidator : AbstractValidator<LogQueryRequest>
{
    public LogQueryValidator()
    {
        RuleForEach(x => x.Level)
            .Must(BeValidLevel).WithMessage("Invalid level. Must be one of: verbose, debug, info, warning, error, fatal.")
            .When(x => x.Level != null);

        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 200).WithMessage("Limit must be between 1 and 200.");

        RuleFor(x => x.From)
            .LessThan(x => x.To).WithMessage("'from' must be before 'to'.")
            .When(x => x.From.HasValue && x.To.HasValue);

        RuleFor(x => x)
            .Must(x => !x.From.HasValue || !x.To.HasValue || (x.To.Value - x.From.Value).TotalDays <= 90)
            .WithMessage("Range cannot exceed 90 days.")
            .When(x => x.From.HasValue && x.To.HasValue);
    }

    private static bool BeValidLevel(string level)
    {
        return level is "verbose" or "debug" or "info" or "warning" or "error" or "fatal";
    }
}

public sealed class StatsQueryValidator : AbstractValidator<StatsQueryRequest>
{
    public StatsQueryValidator()
    {
        RuleFor(x => x.From)
            .LessThan(x => x.To).WithMessage("'from' must be before 'to'.")
            .When(x => x.From.HasValue && x.To.HasValue);

        RuleFor(x => x)
            .Must(x => !x.From.HasValue || !x.To.HasValue || (x.To.Value - x.From.Value).TotalDays <= 90)
            .WithMessage("Range cannot exceed 90 days.")
            .When(x => x.From.HasValue && x.To.HasValue);
    }
}
