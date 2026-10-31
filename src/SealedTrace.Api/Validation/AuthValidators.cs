using Arovyx.SealedTrace.Core.Dtos.Auth;
using FluentValidation;

namespace Arovyx.SealedTrace.Api.Validation;

public sealed class AuthRegisterValidator : AbstractValidator<AuthRegisterRequest>
{
    public AuthRegisterValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email is required.")
            .MaximumLength(320).WithMessage("Email must not exceed 320 characters.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(12).WithMessage("Password must be at least 12 characters.")
            .MaximumLength(128).WithMessage("Password must not exceed 128 characters.");

        RuleFor(x => x.Name)
            .MaximumLength(120).WithMessage("Name must not exceed 120 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Name));
    }
}

public sealed class AuthLoginValidator : AbstractValidator<AuthLoginRequest>
{
    public AuthLoginValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email is required.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}
