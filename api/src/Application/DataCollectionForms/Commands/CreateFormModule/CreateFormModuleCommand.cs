using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.DataCollectionForms.Audit;
using Application.DataCollectionForms.Exceptions;
using Domain.AuditLogs;
using Domain.DataCollectionForms;
using LanguageExt;
using MediatR;

namespace Application.DataCollectionForms.Commands.CreateFormModule;

public record CreateFormModuleCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public string? Code { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
}

public class CreateFormModuleCommandHandler(
    IFormModuleRepository moduleRepository,
    IFormModuleQueries moduleQueries,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<CreateFormModuleCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        CreateFormModuleCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormModuleForbiddenException(FormModuleId.Empty(), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        CreateFormModuleCommand request,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        var code = request.Code!.Trim();

        var existing = await moduleQueries.GetByCodeAsync(code, cancellationToken);
        if (existing.IsSome)
        {
            return new FormModuleDuplicateCodeException(FormModuleId.Empty(), code);
        }

        var moduleId = FormModuleId.New();

        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<FormModuleException, FormModule>(
            async token =>
            {
                try
                {
                    var activeModules = await moduleQueries.GetAllActiveAsync(token);
                    var displayOrder = activeModules.Count + 1;

                    var module = FormModule.Create(
                        moduleId,
                        code,
                        request.Name!,
                        displayOrder,
                        actorId,
                        dateTimeProvider.GetUtcNow(),
                        request.Description);

                    var saved = await moduleRepository.AddAsync(module, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.DataCollectionForm,
                            saved.Id.Value,
                            AuditAction.DataCollectionFormModuleCreated,
                            before: null,
                            after: DataCollectionFormSnapshot.Serialize(saved)),
                        token);

                    return saved;
                }
                catch (Exception exception)
                {
                    return new FormModuleUnknownException(moduleId, exception);
                }
            },
            cancellationToken);
    }
}