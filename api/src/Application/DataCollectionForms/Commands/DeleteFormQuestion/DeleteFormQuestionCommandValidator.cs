using FluentValidation;

namespace Application.DataCollectionForms.Commands.DeleteFormQuestion;

public class DeleteFormQuestionCommandValidator : AbstractValidator<DeleteFormQuestionCommand>
{
    public DeleteFormQuestionCommandValidator()
    {
        RuleFor(x => x.FormModuleId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
    }
}