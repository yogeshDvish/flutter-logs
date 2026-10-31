using FluentValidation;

namespace FlutterLogs.Api.Features.Projects;

public sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(request => request.Name)
            .NotEmpty()
            .MaximumLength(80);

        RuleFor(request => request.Description)
            .MaximumLength(500);
    }
}

public sealed class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(request => request)
            .Must(request => request.Name is not null
                || request.Description is not null
                || request.IngestEnabled is not null)
            .WithMessage("Provide at least one of name, description, or ingestEnabled.");

        RuleFor(request => request.Name)
            .NotEmpty()
            .MaximumLength(80)
            .When(request => request.Name is not null);

        RuleFor(request => request.Description)
            .MaximumLength(500)
            .When(request => request.Description is not null);
    }
}
