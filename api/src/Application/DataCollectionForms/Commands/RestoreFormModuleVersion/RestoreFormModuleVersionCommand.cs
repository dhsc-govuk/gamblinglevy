using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.DataCollectionForms.Audit;
using Application.DataCollectionForms.Exceptions;
using Application.DataCollectionForms.Versions;
using Domain.AuditLogs;
using Domain.DataCollectionForms;
using Domain.Forms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.DataCollectionForms.Commands.RestoreFormModuleVersion;

// BLUM-9102: replaces a module's draft content with what an earlier version held, behind BLUM_9087 (the endpoint is
// gated). A published module is reopened as a draft first; an archived one is refused. The version itself is never
// touched: publishing the restored draft saves the NEXT version number, so history only grows.
//
// The draft's rows are replaced with new ids. Answers are keyed by question code, never by row id, so nothing that
// points at a question is lost; and the removal is saved before the additions, so a code that is removed and added
// back never meets itself in one statement batch.
//
// What a saved version published stays resolvable (BLUM-9105): a question, or an option value, that the latest
// version holding it published but the restored version lacks comes back withdrawn (inactive), from that latest
// definition. LAs never see it, but answers older reports gave keep their label, and a value keeps its meaning.
public record RestoreFormModuleVersionCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public Guid ModuleId { get; init; }
    public int VersionNumber { get; init; }
}

public class RestoreFormModuleVersionCommandHandler(
    IFormModuleRepository moduleRepository,
    IFormModuleQueries moduleQueries,
    IFormModuleVersionQueries versionQueries,
    PublishedQuestionRules publishedQuestionRules,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<RestoreFormModuleVersionCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        RestoreFormModuleVersionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormModuleForbiddenException(FormModuleId.From(request.ModuleId), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        RestoreFormModuleVersionCommand request,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        var moduleId = FormModuleId.From(request.ModuleId);

        var lookup = await moduleQueries.GetByIdWithChildrenAsync(moduleId, cancellationToken);
        if (lookup.IsNone)
        {
            return new FormModuleNotFoundException(moduleId);
        }

        var module = lookup.ValueUnsafe();

        if (module.Status.ToString() == FormStatuses.Archived)
        {
            return new FormModuleArchivedException(moduleId);
        }

        var versionLookup = await versionQueries.GetAsync(moduleId, request.VersionNumber, cancellationToken);
        if (versionLookup.IsNone)
        {
            return new FormModuleVersionNotFoundException(moduleId, request.VersionNumber);
        }

        var version = versionLookup.ValueUnsafe();
        var published = await publishedQuestionRules.LatestPublishedQuestionsAsync(moduleId, cancellationToken);

        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<FormModuleException, FormModule>(
            async token =>
            {
                try
                {
                    var definition = FormModuleDefinition.Parse(version.Definition);
                    var utcNow = dateTimeProvider.GetUtcNow();
                    var before = DataCollectionFormSnapshot.Serialize(module);
                    var reopened = module.Status.IsPublished;

                    if (reopened)
                    {
                        module.StartDraft(actorId, utcNow);
                    }

                    foreach (var question in module.Questions.ToList())
                    {
                        module.RemoveQuestion(question.Id, utcNow);
                    }

                    foreach (var section in module.Sections.ToList())
                    {
                        module.RemoveSection(section.Id, utcNow);
                    }

                    await moduleRepository.UpdateAsync(module, token);

                    module.UpdateDetails(definition.Name, definition.Description, actorId, utcNow);

                    var sectionIds = new Dictionary<Guid, DcFormSectionId>();
                    foreach (var stored in definition.Sections)
                    {
                        var section = module.AddSection(
                            DcFormSectionId.New(),
                            stored.SectionNumber,
                            stored.Title,
                            actorId,
                            utcNow,
                            stored.Description,
                            stored.HelpText);
                        if (!stored.IsActive)
                        {
                            section.Deactivate();
                        }

                        sectionIds[stored.Id] = section.Id;
                    }

                    foreach (var stored in definition.Questions)
                    {
                        var question = module.AddQuestion(
                            DcFormQuestionId.New(),
                            stored.Code,
                            stored.Label,
                            QuestionType.From(stored.QuestionType),
                            stored.DisplayOrder,
                            actorId,
                            utcNow,
                            stored.SectionId is { } sectionId && sectionIds.TryGetValue(sectionId, out var newSectionId) ? newSectionId : null,
                            stored.IsRequired,
                            stored.Hint,
                            stored.HelpText,
                            stored.ErrorMessage,
                            stored.MaxLength,
                            stored.MaxLengthErrorMessage);
                        foreach (var option in stored.Options)
                        {
                            question.AddOption(DcFormQuestionOptionId.New(), option.Value, option.Label, option.DisplayOrder, option.IsActive);
                        }

                        if (!stored.IsActive)
                        {
                            question.Deactivate();
                        }
                    }

                    KeepWhatWasPublished(module, published, actorId, utcNow);

                    await moduleRepository.UpdateAsync(module, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.DataCollectionForm,
                            moduleId.Value,
                            AuditAction.DataCollectionFormModuleVersionRestored,
                            before,
                            JsonSerializer.Serialize(new
                            {
                                moduleId = moduleId.Value,
                                code = module.Code,
                                restoredVersionId = version.Id.Value,
                                restoredVersionNumber = version.VersionNumber,
                                reopenedFromPublished = reopened,
                                module = JsonDocument.Parse(DataCollectionFormSnapshot.Serialize(module)).RootElement,
                            })),
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

    private static void KeepWhatWasPublished(
        FormModule module,
        IReadOnlyDictionary<string, FormQuestionDefinition> published,
        Domain.Users.UserId actorId,
        DateTime utcNow)
    {
        var nextOrder = module.Questions.Select(q => q.DisplayOrder).DefaultIfEmpty(0).Max();
        foreach (var (code, stored) in published)
        {
            var question = module.Questions.FirstOrDefault(q => string.Equals(q.Code, code, StringComparison.OrdinalIgnoreCase));
            if (question is null)
            {
                // Withdrawn, and outside any section: its section may not exist in the restored version.
                question = module.AddQuestion(
                    DcFormQuestionId.New(),
                    stored.Code,
                    stored.Label,
                    QuestionType.From(stored.QuestionType),
                    ++nextOrder,
                    actorId,
                    utcNow,
                    sectionId: null,
                    stored.IsRequired,
                    stored.Hint,
                    stored.HelpText,
                    stored.ErrorMessage,
                    stored.MaxLength,
                    stored.MaxLengthErrorMessage);

                // Its options as published, so an older answer's value still finds its label among the active ones.
                foreach (var option in stored.Options)
                {
                    question.AddOption(DcFormQuestionOptionId.New(), option.Value, option.Label, option.DisplayOrder, option.IsActive);
                }

                question.Deactivate();
                continue;
            }

            if (!question.QuestionType.IsChoice)
            {
                continue;
            }

            var values = question.Options.Select(o => o.Value).ToHashSet(StringComparer.Ordinal);
            var nextOption = question.Options.Select(o => o.DisplayOrder).DefaultIfEmpty(0).Max();
            foreach (var option in stored.Options.Where(o => !values.Contains(o.Value)))
            {
                question.AddOption(DcFormQuestionOptionId.New(), option.Value, option.Label, ++nextOption, isActive: false);
            }
        }
    }
}