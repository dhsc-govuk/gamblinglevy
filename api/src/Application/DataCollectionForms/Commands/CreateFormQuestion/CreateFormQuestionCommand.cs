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

namespace Application.DataCollectionForms.Commands.CreateFormQuestion;

// Adds a question (with options for choice types) to a forms-builder module. Code is the stable answer key,
// unique within the module. Admin-only.
public record CreateFormQuestionCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public Guid FormModuleId { get; init; }
    public Guid? SectionId { get; init; }
    public string? Code { get; init; }
    public string? Label { get; init; }
    public string? QuestionType { get; init; }
    public bool IsRequired { get; init; }
    public string? Hint { get; init; }
    public string? HelpText { get; init; }
    public IReadOnlyList<QuestionOptionInput> Options { get; init; } = [];
}

public class CreateFormQuestionCommandHandler(
    IFormModuleQueries formModuleQueries,
    IFormModuleRepository formModuleRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PublishedQuestionRules publishedQuestionRules,
    IFeatureFlags featureFlags,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<CreateFormQuestionCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        CreateFormQuestionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormModuleForbiddenException(Domain.DataCollectionForms.FormModuleId.Empty(), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        CreateFormQuestionCommand request,
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

        var code = request.Code!.Trim();
        var existing = module.Questions.FirstOrDefault(q => string.Equals(q.Code, code, StringComparison.OrdinalIgnoreCase));
        if (existing is { IsActive: true })
        {
            return new DcFormQuestionDuplicateCodeException(moduleId, code);
        }

        // BLUM-9105: answers are joined across versions by code, so a code a published version used stays that question's.
        // Asked before the duplicate check for a withdrawn question, which still holds its code: "retired" says why.
        if (featureFlags.IsEnabled(Feature.BLUM_9087)
            && (await publishedQuestionRules.PublishedCodesAsync(moduleId, cancellationToken)).Contains(code))
        {
            return new DcFormQuestionCodeRetiredException(moduleId, code);
        }

        if (existing is not null)
        {
            return new DcFormQuestionDuplicateCodeException(moduleId, code);
        }

        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<FormModuleException, FormModule>(
            async token =>
            {
                try
                {
                    var questionType = Domain.Forms.QuestionType.From(request.QuestionType!);
                    // Use max+1 (not count+1) so a new question never reuses the display order of an existing one
                    // after a question has been deleted (deletion leaves gaps but never renumbers).
                    var displayOrder = module.Questions.Count > 0 ? module.Questions.Max(q => q.DisplayOrder) + 1 : 1;
                    var sectionId = request.SectionId.HasValue ? new DcFormSectionId(request.SectionId.Value) : null;

                    var question = module.AddQuestion(
                        DcFormQuestionId.New(),
                        code,
                        request.Label!,
                        questionType,
                        displayOrder,
                        actorId,
                        dateTimeProvider.GetUtcNow(),
                        sectionId,
                        request.IsRequired,
                        request.Hint,
                        request.HelpText);

                    if (questionType.IsChoice)
                    {
                        for (var idx = 0; idx < request.Options.Count; idx++)
                        {
                            var option = request.Options[idx];
                            question.AddOption(DcFormQuestionOptionId.New(), option.Value, option.Label, idx + 1);
                        }
                    }

                    await formModuleRepository.UpdateAsync(module, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.DataCollectionForm,
                            question.Id.Value,
                            AuditAction.DataCollectionFormQuestionCreated,
                            before: null,
                            after: DataCollectionFormSnapshot.Serialize(question)),
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