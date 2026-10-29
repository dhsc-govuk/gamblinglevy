using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.DataCollectionForms.Audit;
using Application.DataCollectionForms.Exceptions;
using Domain.AuditLogs;
using Domain.DataCollectionForms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.DataCollectionForms.Commands.CreateFormSection;

// Adds a section to a forms-builder module. Returns the parent module so the controller can re-emit the full tree.
public record CreateFormSectionCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public Guid FormModuleId { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? HelpText { get; init; }
}

public class CreateFormSectionCommandHandler(
    IFormModuleQueries formModuleQueries,
    IFormModuleRepository formModuleRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<CreateFormSectionCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        CreateFormSectionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormModuleForbiddenException(new FormModuleId(request.FormModuleId), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        CreateFormSectionCommand request,
        Domain.Users.UserId actorId,
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

        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<FormModuleException, FormModule>(
            async token =>
            {
                try
                {
                    // Use max+1 (not count+1) so a new section never reuses the number of an existing one after a
                    // middle section has been deleted (deletion leaves gaps but never renumbers).
                    var sectionNumber = module.Sections.Count > 0 ? module.Sections.Max(s => s.SectionNumber) + 1 : 1;
                    var section = module.AddSection(
                        DcFormSectionId.New(),
                        sectionNumber,
                        request.Title!,
                        actorId,
                        dateTimeProvider.GetUtcNow(),
                        request.Description,
                        request.HelpText);
                    await formModuleRepository.UpdateAsync(module, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.DataCollectionForm,
                            section.Id.Value,
                            AuditAction.DataCollectionFormSectionCreated,
                            before: null,
                            after: DataCollectionFormSnapshot.Serialize(section)),
                        token);

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
            },
            cancellationToken);
    }
}