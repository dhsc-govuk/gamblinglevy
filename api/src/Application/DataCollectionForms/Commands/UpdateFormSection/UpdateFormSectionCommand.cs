using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.DataCollectionForms.Exceptions;
using Domain.DataCollectionForms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.DataCollectionForms.Commands.UpdateFormSection;

// Updates a section's editable details. Returns the parent module so the controller can re-emit the full tree.
public record UpdateFormSectionCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public Guid FormModuleId { get; init; }
    public Guid SectionId { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? HelpText { get; init; }
}

public class UpdateFormSectionCommandHandler(
    IFormModuleQueries formModuleQueries,
    IFormModuleRepository formModuleRepository,
    PermissionsService permissionsService)
    : IRequestHandler<UpdateFormSectionCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        UpdateFormSectionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            _ => HandleWithActor(request, cancellationToken),
            error => new FormModuleForbiddenException(new FormModuleId(request.FormModuleId), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        UpdateFormSectionCommand request,
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
        var section = module.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section is null)
        {
            return new DcFormSectionNotFoundException(moduleId, sectionId);
        }

        try
        {
            section.UpdateDetails(request.Title!, request.Description, request.HelpText);
            await formModuleRepository.UpdateAsync(module, cancellationToken);
            return module;
        }
        catch (Exception exception)
        {
            return new FormModuleUnknownException(moduleId, exception);
        }
    }
}