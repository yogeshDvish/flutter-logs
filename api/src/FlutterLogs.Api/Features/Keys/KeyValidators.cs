using FluentValidation;

namespace FlutterLogs.Api.Features.Keys;

public sealed class CreateProjectKeyRequestValidator : AbstractValidator<CreateProjectKeyRequest>
{
    public CreateProjectKeyRequestValidator()
    {
        RuleFor(request => request.Label)
            .MaximumLength(60);

        RuleFor(request => request.GraceHours)
            .InclusiveBetween(0, 24)
            .When(request => request.GraceHours is not null);
    }
}
