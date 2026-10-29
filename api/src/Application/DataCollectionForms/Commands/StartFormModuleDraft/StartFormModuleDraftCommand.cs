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

namespace Application.DataCollectionForms.Commands.StartFormModuleDraft;

// BLUM-9102: reopens a published module for editing, behind BLUM_9087 (the endpoint is gated). The draft is edited
// in place: what was published survives as the module's latest FormModuleVersion — saved by every publish, and by
// FormModuleVersionBackfill as version 1 for a form published before versioning — and the next publish decides
// whether the draft became a new version.
public record StartFormModuleDraftCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public Guid Id { get; init; }
}

public class StartFormModuleDraftCommandHandler(
    IFormModuleRepository moduleRepository,
    IFormModuleQueries moduleQueries,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<StartFormModuleDraftCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        StartFormModuleDraftCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormModuleForbiddenException(FormModuleId.From(request.Id), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        StartFormModuleDraftCommand request,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        var moduleId = FormModuleId.From(request.Id);

        var lookup = await moduleQueries.GetByIdWithChildrenAsync(moduleId, cancellationToken);
        if (lookup.IsNone)
        {
            return new FormModuleNotFoundException(moduleId);
        }

        var module = lookup.ValueUnsafe();

        if (!module.Status.IsPublished)
        {
            return new Application.DataCollectionForms.Exceptions.FormModuleNotPublishedException(moduleId);
        }

        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<FormModuleException, FormModule>(
            async token =>
            {
                try
                {
                    var before = DataCollectionFormSnapshot.Serialize(module);
                    module.StartDraft(actorId, dateTimeProvider.GetUtcNow());
                    await moduleRepository.UpdateAsync(module, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.DataCollectionForm,
                            moduleId.Value,
                            AuditAction.DataCollectionFormModuleDraftStarted,
                            before,
                            DataCollectionFormSnapshot.Serialize(module)),
                        token);

                    return module;
                }
                catch (Domain.DataCollectionForms.FormModuleNotPublishedException)
                {
                    return new Application.DataCollectionForms.Exceptions.FormModuleNotPublishedException(moduleId);
                }
                catch (Exception exception)
                {
                    return new FormModuleUnknownException(moduleId, exception);
                }
            },
            cancellationToken);
    }
}