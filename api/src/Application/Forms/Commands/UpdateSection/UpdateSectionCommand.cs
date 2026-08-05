using Application.Common.Interfaces.Repositories;
using Application.Forms.Exceptions;
using Domain.Categories;
using Domain.Forms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Forms.Commands.UpdateSection;

public record UpdateSectionCommand : IRequest<Either<FormDesignerException, FormSection>>
{
    public Guid Id { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public Guid? CategoryId { get; init; }
}

public class UpdateSectionCommandHandler(IFormSectionRepository sectionRepository)
    : IRequestHandler<UpdateSectionCommand, Either<FormDesignerException, FormSection>>
{
    public async Task<Either<FormDesignerException, FormSection>> Handle(
        UpdateSectionCommand request,
        CancellationToken cancellationToken)
    {
        var sectionId = new FormSectionId(request.Id);
        var lookup = await sectionRepository.GetByIdAsync(sectionId, cancellationToken);
        if (lookup.IsNone)
        {
            return new FormSectionNotFoundException(sectionId);
        }

        try
        {
            var section = lookup.ValueUnsafe();
            section.UpdateDetails(request.Title!, request.Description);
            section.SetCategory(request.CategoryId.HasValue ? new CategoryId(request.CategoryId.Value) : null);
            return await sectionRepository.UpdateAsync(section, cancellationToken);
        }
        catch (Exception exception)
        {
            return new FormDesignerUnknownException(exception);
        }
    }
}