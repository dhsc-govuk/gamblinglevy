using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Forms.Exceptions;
using Domain.Categories;
using Domain.Forms;
using LanguageExt;
using MediatR;

namespace Application.Forms.Commands.CreateSection;

public record CreateSectionCommand : IRequest<Either<FormDesignerException, FormSection>>
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public Guid? CategoryId { get; init; }
}

public class CreateSectionCommandHandler(
    IFormSectionRepository sectionRepository,
    PermissionsService permissionsService)
    : IRequestHandler<CreateSectionCommand, Either<FormDesignerException, FormSection>>
{
    public async Task<Either<FormDesignerException, FormSection>> Handle(
        CreateSectionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormDesignerException, FormSection>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormDesignerForbiddenException(error.Message));
    }

    private async Task<Either<FormDesignerException, FormSection>> HandleWithActor(
        CreateSectionCommand request,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        try
        {
            var nextOrder = await sectionRepository.GetMaxDisplayOrderAsync(cancellationToken) + 1;
            var categoryId = request.CategoryId.HasValue ? new CategoryId(request.CategoryId.Value) : null;
            var section = FormSection.Create(FormSectionId.New(), request.Title!, nextOrder, actorId, request.Description, categoryId);
            return await sectionRepository.AddAsync(section, cancellationToken);
        }
        catch (Exception exception)
        {
            return new FormDesignerUnknownException(exception);
        }
    }
}