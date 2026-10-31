using Arovyx.SealedTrace.Core.Dtos.Projects;
using FluentValidation;

namespace Arovyx.SealedTrace.Api.Validation;

public sealed class CreateProjectValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Project name is required.")
            .MinimumLength(2).WithMessage("Project name must be at least 2 characters.")
            .MaximumLength(120).WithMessage("Project name must not exceed 120 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.Environment)
            .NotEmpty().WithMessage("Environment is required.")
            .MaximumLength(16).WithMessage("Environment must not exceed 16 characters.")
            .Must(BeValidEnvironment).WithMessage("Environment must be one of: prod, staging, dev, test.");
    }

    private static bool BeValidEnvironment(string environment)
    {
        return environment is "prod" or "staging" or "dev" or "test";
    }
}

public sealed class UpdateProjectValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectValidator()
    {
        RuleFor(x => x.Name)
            .MinimumLength(2).WithMessage("Project name must be at least 2 characters.")
            .MaximumLength(120).WithMessage("Project name must not exceed 120 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Name));

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description must not exceed 500 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.Environment)
            .NotEmpty().WithMessage("Environment is required.")
            .MaximumLength(16).WithMessage("Environment must not exceed 16 characters.")
            .Must(BeValidEnvironment).WithMessage("Environment must be one of: prod, staging, dev, test.")
            .When(x => !string.IsNullOrWhiteSpace(x.Environment));

        RuleFor(x => x.RetentionMaxAgeDays)
            .GreaterThan(0).WithMessage("Retention max age days must be greater than 0.")
            .When(x => x.RetentionMaxAgeDays is not null);

        RuleFor(x => x.RetentionMaxEvents)
            .GreaterThan(0).WithMessage("Retention max events must be greater than 0.")
            .When(x => x.RetentionMaxEvents is not null);
    }

    private static bool BeValidEnvironment(string environment)
    {
        return environment is "prod" or "staging" or "dev" or "test";
    }
}
