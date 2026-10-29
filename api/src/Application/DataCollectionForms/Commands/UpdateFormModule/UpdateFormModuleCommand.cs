using Application.Common.Features;
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

namespace Application.DataCollectionForms.Commands.UpdateFormModule;

public record UpdateFormModuleCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public Guid Id { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
}

public class UpdateFormModuleCommandHandler(
    IFormModuleRepository moduleRepository,
    IFormModuleQueries moduleQueries,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    IFeatureFlags featureFlags,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<UpdateFormModuleCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        UpdateFormModuleCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormModuleForbiddenException(FormModuleId.From(request.Id), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        UpdateFormModuleCommand request,
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

        // BLUM-9102: with versioning on, a published form changes only through a draft, so its name and description
        // are part of the next version rather than an untracked edit to the live form. Like the other structural edits.
        if (featureFlags.IsEnabled(Feature.BLUM_9087) && module.Status.IsPublished)
        {
            return new FormModuleNotEditableException(moduleId);
        }

        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<FormModuleException, FormModule>(
            async token =>
            {
                try
                {
                    var before = DataCollectionFormSnapshot.Serialize(module);
                    module.UpdateDetails(request.Name!, request.Description, actorId, dateTimeProvider.GetUtcNow());
                    await moduleRepository.UpdateAsync(module, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.DataCollectionForm,
                            moduleId.Value,
                            AuditAction.DataCollectionFormModuleUpdated,
                            before,
                            DataCollectionFormSnapshot.Serialize(module)),
                        token);

                    return module;
                }
                catch (Exception exception)
                {
                    return new FormModuleUnknownException(moduleId, exception);
                }
            },
            cancellationToken);
    }
}