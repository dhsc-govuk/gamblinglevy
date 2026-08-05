using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.DataCollectionForms.Exceptions;
using Domain.DataCollectionForms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.DataCollectionForms.Commands.ReorderFormSections;

// Reorders a module's sections from the supplied ordered id list (section number = list index + 1). Returns the
// parent module so the controller can re-emit the full tree.
public record ReorderFormSectionsCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public Guid FormModuleId { get; init; }
    public IReadOnlyList<Guid> OrderedSectionIds { get; init; } = [];
}

public class ReorderFormSectionsCommandHandler(
    IFormModuleQueries formModuleQueries,
    IFormModuleRepository formModuleRepository,
    PermissionsService permissionsService)
    : IRequestHandler<ReorderFormSectionsCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        ReorderFormSectionsCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            _ => HandleWithActor(request, cancellationToken),
            error => new FormModuleForbiddenException(new FormModuleId(request.FormModuleId), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        ReorderFormSectionsCommand request,
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

        try
        {
            for (var index = 0; index < request.OrderedSectionIds.Count; index++)
            {
                var sectionId = new DcFormSectionId(request.OrderedSectionIds[index]);
                var section = module.Sections.FirstOrDefault(s => s.Id == sectionId);
                if (section is null)
                {
                    return new DcFormSectionNotFoundException(moduleId, sectionId);
                }

                section.UpdateSectionNumber(index + 1);
            }

            await formModuleRepository.UpdateAsync(module, cancellationToken);
            return module;
        }
        catch (Exception exception)
        {
            return new FormModuleUnknownException(moduleId, exception);
        }
    }
}