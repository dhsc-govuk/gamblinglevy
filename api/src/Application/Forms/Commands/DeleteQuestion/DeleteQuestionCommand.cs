using Application.Common.Interfaces.Repositories;
using Application.Forms.Exceptions;
using Domain.Forms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;
using Unit = LanguageExt.Unit;

namespace Application.Forms.Commands.DeleteQuestion;

// Hard-deletes a question and its options. Existing answers are intentionally preserved (they reference the
// question by id with a snapshotted code/value). Blocked when another question's conditional rule depends on it.
public record DeleteQuestionCommand : IRequest<Either<FormDesignerException, Unit>>
{
    public Guid Id { get; init; }
}

public class DeleteQuestionCommandHandler(IFormQuestionRepository questionRepository)
    : IRequestHandler<DeleteQuestionCommand, Either<FormDesignerException, Unit>>
{
    public async Task<Either<FormDesignerException, Unit>> Handle(
        DeleteQuestionCommand request,
        CancellationToken cancellationToken)
    {
        var questionId = new FormQuestionId(request.Id);
        var lookup = await questionRepository.GetByIdAsync(questionId, cancellationToken);
        if (lookup.IsNone)
        {
            return new FormQuestionNotFoundException(questionId);
        }

        var question = lookup.ValueUnsafe();
        var referenced = await questionRepository.AnyReferencingConditionalAsync(question.Code, questionId, cancellationToken);
        if (referenced)
        {
            return new FormQuestionReferencedException(questionId, question.Code);
        }

        try
        {
            await questionRepository.DeleteAsync(questionId, cancellationToken);
            return Unit.Default;
        }
        catch (Exception exception)
        {
            return new FormDesignerUnknownException(exception);
        }
    }
}