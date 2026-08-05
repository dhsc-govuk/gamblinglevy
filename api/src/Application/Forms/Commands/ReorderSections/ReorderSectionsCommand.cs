using Application.Common.Interfaces.Repositories;
using Application.Forms.Exceptions;
using LanguageExt;
using MediatR;
using Unit = LanguageExt.Unit;

namespace Application.Forms.Commands.ReorderSections;

// Reorders sections from the supplied ordered id list (display order = list index).
public record ReorderSectionsCommand : IRequest<Either<FormDesignerException, Unit>>
{
    public IReadOnlyList<Guid> OrderedIds { get; init; } = [];
}

public class ReorderSectionsCommandHandler(IFormSectionRepository sectionRepository)
    : IRequestHandler<ReorderSectionsCommand, Either<FormDesignerException, Unit>>
{
    public async Task<Either<FormDesignerException, Unit>> Handle(
        ReorderSectionsCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            await sectionRepository.ReorderAsync(request.OrderedIds, cancellationToken);
            return Unit.Default;
        }
        catch (Exception exception)
        {
            return new FormDesignerUnknownException(exception);
        }
    }
}