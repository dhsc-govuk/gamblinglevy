using Application.Common.Features;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.DataCollectionForms.Audit;
using Application.DataCollectionForms.Exceptions;
using Application.DataCollectionForms.Versions;
using Domain.AuditLogs;
using Domain.DataCollectionForms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.DataCollectionForms.Commands.DeleteFormQuestion;

// Removes a question (and its owned options) from a forms-builder module. Admin-only.
//
// BLUM-9102: with versioning on, a question a saved version holds is withdrawn rather than deleted. Readers that
// still read the live form (the activity-record export, a collection with no recorded version) need it to label the
// answers older reports gave, and BLUM-8340's rule is to deactivate published questions, never delete them. It leaves
// its section, so the section can still be deleted. Every LA read skips an inactive question.
public record DeleteFormQuestionCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public Guid FormModuleId { get; init; }
    public Guid Id { get; init; }
}

public class DeleteFormQuestionCommandHandler(
    IFormModuleQueries formModuleQueries,
    IFormModuleRepository formModuleRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PublishedQuestionRules publishedQuestionRules,
    IFeatureFlags featureFlags,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<DeleteFormQuestionCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        DeleteFormQuestionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormModuleForbiddenException(Domain.DataCollectionForms.FormModuleId.Empty(), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        DeleteFormQuestionCommand request,
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

        var questionId = new DcFormQuestionId(request.Id);
        var question = module.Questions.FirstOrDefault(q => q.Id == questionId);
        if (question is null)
        {
            return new DcFormQuestionNotFoundException(moduleId, questionId);
        }

        var withdraw = featureFlags.IsEnabled(Feature.BLUM_9087)
            && (await publishedQuestionRules.PublishedCodesAsync(moduleId, cancellationToken)).Contains(question.Code);

        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<FormModuleException, FormModule>(
            async token =>
            {
                try
                {
                    var before = DataCollectionFormSnapshot.Serialize(question);
                    var utcNow = dateTimeProvider.GetUtcNow();
                    if (withdraw)
                    {
                        if (!module.WithdrawQuestion(questionId, utcNow))
                        {
                            // Already withdrawn: nothing changes, so no second audit row.
                            return module;
                        }
                    }
                    else
                    {
                        module.RemoveQuestion(questionId, utcNow);
                    }

                    await formModuleRepository.UpdateAsync(module, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.DataCollectionForm,
                            questionId.Value,
                            AuditAction.DataCollectionFormQuestionDeleted,
                            before,
                            after: withdraw ? DataCollectionFormSnapshot.Serialize(question) : null),
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