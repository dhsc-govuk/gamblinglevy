using FluentValidation;

namespace Application.Submissions.Commands.CompleteFundingSpent;

public class CompleteFundingSpentCommandValidator : AbstractValidator<CompleteFundingSpentCommand>
{
    public CompleteFundingSpentCommandValidator()
    {
        RuleFor(x => x.CollectionId).NotEmpty();
    }
}