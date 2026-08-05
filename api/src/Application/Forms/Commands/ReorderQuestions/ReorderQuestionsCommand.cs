using Application.Common.Interfaces.Repositories;
using Application.Forms.Exceptions;
using Domain.Forms;
using LanguageExt;
using MediatR;
using Unit = LanguageExt.Unit;

namespace Application.Forms.Commands.ReorderQuestions;

// Reorders the questions within a section from the supplied ordered id list (display order = list index).
public record ReorderQuestionsCommand : IRequest<Either<FormDesignerException, Unit>>
{
    public Guid SectionId { get; init; }
    public IReadOnlyList<Guid> OrderedIds { get; init; } = [];
}

public class ReorderQuestionsCommandHandler(IFormQuestionRepository questionRepository)
    : IRequestHandler<ReorderQuestionsCommand, Either<FormDesignerException, Unit>>
{
    public async Task<Either<FormDesignerException, Unit>> Handle(
        ReorderQuestionsCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            await questionRepository.ReorderAsync(new FormSectionId(request.SectionId), request.OrderedIds, cancellationToken);
            return Unit.Default;
        }
        catch (Exception exception)
        {
            return new FormDesignerUnknownException(exception);
        }
    }
}