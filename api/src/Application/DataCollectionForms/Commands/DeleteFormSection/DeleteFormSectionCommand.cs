using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.DataCollectionForms.Exceptions;
using Domain.DataCollectionForms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.DataCollectionForms.Commands.DeleteFormSection;

// Removes a section from a forms-builder module. Returns the parent module so the controller can re-emit the full tree.
public record DeleteFormSectionCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public Guid FormModuleId { get; init; }
    public Guid SectionId { get; init; }
}

public class DeleteFormSectionCommandHandler(
    IFormModuleQueries formModuleQueries,
    IFormModuleRepository formModuleRepository,
    PermissionsService permissionsService)
    : IRequestHandler<DeleteFormSectionCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        DeleteFormSectionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            _ => HandleWithActor(request, cancellationToken),
            error => new FormModuleForbiddenException(new FormModuleId(request.FormModuleId), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        DeleteFormSectionCommand request,
        CancellationToken cancellationToken)
    {
        var moduleId = new FormModuleId(request.FormModuleId);
        var lookup = await formModuleQueries.GetByIdWithChildrenAsync(moduleId, cancellationToken);
        if (lookup.IsNone)
        {
            return new FormModuleNotFoundException(moduleId);
        }

        var module = lookup.ValueUnsafe();
        if (module.Status.IsPublished)
        {
            return new FormModuleNotEditableException(module.Id);
        }

        var sectionId = new DcFormSectionId(request.SectionId);
        if (module.Sections.All(s => s.Id != sectionId))
        {
            return new DcFormSectionNotFoundException(moduleId, sectionId);
        }

        if (module.Questions.Any(q => q.SectionId == sectionId))
        {
            return new DcFormSectionNotEmptyException(module.Id, sectionId);
        }

        try
        {
            module.RemoveSection(sectionId);
            await formModuleRepository.UpdateAsync(module, cancellationToken);
            return module;
        }
        catch (FormModulePublishedException)
        {
            return new FormModuleNotEditableException(moduleId);
        }
        catch (Exception exception)
        {
            return new FormModuleUnknownException(moduleId, exception);
        }
    }
}