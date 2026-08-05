using Application.Common.Interfaces.Repositories;
using Application.Forms.Exceptions;
using Domain.Forms;
using LanguageExt;
using MediatR;
using Unit = LanguageExt.Unit;

namespace Application.Forms.Commands.DeleteSection;

// Hard-deletes a section, but only when it contains no questions. A populated section is retired by
// deactivating it instead (BLUM-6444).
public record DeleteSectionCommand : IRequest<Either<FormDesignerException, Unit>>
{
    public Guid Id { get; init; }
}

public class DeleteSectionCommandHandler(
    IFormSectionRepository sectionRepository,
    IFormQuestionRepository questionRepository)
    : IRequestHandler<DeleteSectionCommand, Either<FormDesignerException, Unit>>
{
    public async Task<Either<FormDesignerException, Unit>> Handle(
        DeleteSectionCommand request,
        CancellationToken cancellationToken)
    {
        var sectionId = new FormSectionId(request.Id);
        var lookup = await sectionRepository.GetByIdAsync(sectionId, cancellationToken);
        if (lookup.IsNone)
        {
            return new FormSectionNotFoundException(sectionId);
        }

        var questionCount = await questionRepository.CountBySectionAsync(sectionId, cancellationToken);
        if (questionCount > 0)
        {
            return new FormSectionNotEmptyException(sectionId);
        }

        try
        {
            await sectionRepository.DeleteAsync(sectionId, cancellationToken);
            return Unit.Default;
        }
        catch (Exception exception)
        {
            return new FormDesignerUnknownException(exception);
        }
    }
}