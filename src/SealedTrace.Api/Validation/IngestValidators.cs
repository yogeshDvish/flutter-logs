using Arovyx.SealedTrace.Core.Dtos.Ingest;
using FluentValidation;

namespace Arovyx.SealedTrace.Api.Validation;

public sealed class SealedEnvelopeValidator : AbstractValidator<SealedEnvelope>
{
    public SealedEnvelopeValidator()
    {
        RuleFor(x => x.V)
            .Equal(1).WithMessage("Envelope version must be 1.");

        RuleFor(x => x.Alg)
            .NotEmpty().WithMessage("Algorithm is required.")
            .Equal("RSA-OAEP-2048-SHA256+AES-256-GCM")
            .WithMessage("Unsupported algorithm.");

        RuleFor(x => x.ProjectId)
            .NotEqual(Guid.Empty).WithMessage("Project ID is required.");

        RuleFor(x => x.CreatedAt)
            .NotEmpty().WithMessage("CreatedAt is required.");

        RuleFor(x => x.EventCount)
            .GreaterThan(0).WithMessage("Event count must be greater than 0.");

        RuleFor(x => x.WrappedKey)
            .NotEmpty().WithMessage("Wrapped key is required.");

        RuleFor(x => x.Iv)
            .NotEmpty().WithMessage("IV is required.");

        RuleFor(x => x.Tag)
            .NotEmpty().WithMessage("Tag is required.");

        RuleFor(x => x.Ciphertext)
            .NotEmpty().WithMessage("Ciphertext is required.");
    }
}

public sealed class IngestRequestValidator : AbstractValidator<IngestRequest>
{
    public IngestRequestValidator()
    {
        RuleFor(x => x.Envelopes)
            .NotNull().WithMessage("Envelopes are required.")
            .NotEmpty().WithMessage("At least one envelope is required.")
            .Must(x => x.Count <= 50).WithMessage("Maximum 50 envelopes per batch.");

        RuleForEach(x => x.Envelopes).SetValidator(new SealedEnvelopeValidator());
    }
}
