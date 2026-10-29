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

namespace Application.DataCollectionForms.Commands.ReorderFormQuestions;

// Reorders the questions on a forms-builder module from the supplied ordered id list (display order = list
// index + 1). Admin-only.
public record ReorderFormQuestionsCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public Guid FormModuleId { get; init; }
    public IReadOnlyList<Guid> OrderedQuestionIds { get; init; } = [];
}

public class ReorderFormQuestionsCommandHandler(
    IFormModuleQueries formModuleQueries,
    IFormModuleRepository formModuleRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService)
    : IRequestHandler<ReorderFormQuestionsCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        ReorderFormQuestionsCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormModuleForbiddenException(Domain.DataCollectionForms.FormModuleId.Empty(), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        ReorderFormQuestionsCommand request,
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
                    var before = DataCollectionFormSnapshot.SerializeQuestionOrder(module);

                    for (var index = 0; index < request.OrderedQuestionIds.Count; index++)
                    {
                        var questionId = new DcFormQuestionId(request.OrderedQuestionIds[index]);
                        var question = module.Questions.FirstOrDefault(q => q.Id == questionId);
                        if (question is null)
                        {
                            return new DcFormQuestionNotFoundException(moduleId, questionId);
                        }

                        question.SetDisplayOrder(index + 1);
                    }

                    await formModuleRepository.UpdateAsync(module, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.DataCollectionForm,
                            moduleId.Value,
                            AuditAction.DataCollectionFormQuestionsReordered,
                            before,
                            DataCollectionFormSnapshot.SerializeQuestionOrder(module)),
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