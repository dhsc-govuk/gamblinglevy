using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.MaturityAssessments.Protocol;
using Application.Submissions;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.MaturityAssessments;
using Domain.Organisations;
using Domain.Users;
using LanguageExt.UnsafeValueAccess;
using Microsoft.Extensions.Logging;

namespace Application.MaturityAssessments.Prefill;

// The second moment the maturity pre-fill has to run (BLUM-8927). Start pre-fills once, and the task list starts the
// assessment on the LA's first visit — so an LA that opens it BEFORE submitting a quarterly report would never see
// that report's data. Finalising a report is the event that changes "what has this organisation submitted?", which
// is the same reasoning, and the same call site, as ActivityResultCarryForward.CopyIntoOtherOpenSubmissionsAsync.
//
// Called by SubmitReportCommandHandler after the submit is persisted. Best-effort like the carry-forward: the
// submit is already irreversible, so a failure here is logged and the LA simply keeps the assessment they had.
//
// It takes NO CancellationToken (the MfaFailureRecorder rule). This is the only thing that ever pulls a given report
// into an assessment that was already started: a browser that drops the connection while the submit is finishing
// would cancel the request's token, the refresh would be abandoned and swallowed here, and a second submit answers
// 409 — so that report's data would never arrive.
public class MaturityPrefillRefresher(
    ICollectionQueries collectionQueries,
    IMaturityAssessmentQueries assessmentQueries,
    IMaturityAssessmentAnswerRepository answerRepository,
    IMaturityPrefillService prefillService,
    IAuditLogRepository auditLogRepository,
    IDateTimeProvider dateTimeProvider,
    ILogger<MaturityPrefillRefresher> logger)
{
    public async Task RefreshOpenAssessmentsAsync(
        Organisation organisation,
        CollectionId submittedCollectionId,
        UserId actorId)
    {
        try
        {
            var collections = await collectionQueries.GetAssignedToOrganisationAsync(organisation.Id);
            var utcNow = dateTimeProvider.GetUtcNow();

            foreach (var collection in collections.Where(c =>
                !c.IsDraft
                && SubmissionEligibility.IsMaturityAssessment(c)
                && SubmissionEligibility.IsOpen(c, utcNow)
                && SubmissionEligibility.IsAssignedTo(c, organisation)))
            {
                await RefreshAsync(organisation, collection, submittedCollectionId, actorId);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to refresh the maturity-assessment pre-fill of organisation {OrganisationId} after it submitted collection {CollectionId}.",
                organisation.Id,
                submittedCollectionId);
        }
    }

    // Its own try/catch so one assessment's failure — typically the unique index refusing a row because the LA saved
    // that page a moment ago — never costs the organisation's other open assessments their refresh.
    private async Task RefreshAsync(
        Organisation organisation,
        Collection collection,
        CollectionId submittedCollectionId,
        UserId actorId)
    {
        try
        {
            // Only to skip the build when there is nothing to refresh; the state the plan is decided on is read below.
            if (await ReadOpenAssessmentAsync(collection, organisation) is null)
            {
                return;
            }

            // The collection's current forms, as at Start (BLUM-8093).
            var included = MaturityCollectionForms.SectionCodesFor(collection).ToHashSet(StringComparer.Ordinal);
            var questions = MaturityQuestionResolver.ResolveAll().Where(q => included.Contains(q.Section)).ToList();
            if (questions.Count == 0)
            {
                return;
            }

            var prefilled = await prefillService.BuildAsync(organisation, collection, questions, CancellationToken.None);

            // Everything the plan is decided on is read AFTER the slow build. A colleague can complete a section,
            // or submit the whole assessment, while it runs — and a plan made on the earlier read would then rewrite
            // answers they had just checked and signed off.
            if (await ReadOpenAssessmentAsync(collection, organisation) is not { } assessment)
            {
                return;
            }

            var assessmentId = new MaturityAssessmentId(assessment.AssessmentId);
            var existing = await answerRepository.GetForAssessmentAsync(assessmentId);
            var completedSections = assessment.Sections
                .Where(s => s.Status == MaturityAssessmentSectionStatuses.Completed)
                .Select(s => s.SectionCode)
                .ToHashSet(StringComparer.Ordinal);

            var plan = PrefillRefreshPlanner.Plan(questions, completedSections, existing, prefilled);
            if (plan.IsEmpty)
            {
                return;
            }

            // Stamped with the submitting user, as Start stamps its rows with the user who started the assessment.
            var writtenAt = dateTimeProvider.GetUtcNow();
            var rows = plan.Write
                .Select(p => MaturityAssessmentAnswer.Record(
                    MaturityAssessmentAnswerId.New(),
                    assessmentId,
                    p.QuestionCode,
                    p.Value,
                    p.Source,
                    actorId,
                    writtenAt))
                .ToList();

            await answerRepository.ReplaceRowsAsync(plan.Remove, rows);

            // Counts only, never values — the same rule as the Start row this one continues.
            await auditLogRepository.TryAppendAsync(
                AuditLog.Create(
                    actorId,
                    AuditTargetType.MaturityAssessment,
                    assessment.AssessmentId,
                    AuditAction.MaturityAssessmentPrefillRefreshed,
                    before: null,
                    after: JsonSerializer.Serialize(new
                    {
                        collectionId = collection.Id.Value,
                        submittedCollectionId = submittedCollectionId.Value,
                        answersWritten = rows.Count,
                        answersReplaced = plan.Remove.Count,
                    }),
                    organisation.Id));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to refresh the pre-fill of the maturity assessment for collection {CollectionId} and organisation {OrganisationId}.",
                collection.Id,
                organisation.Id);
        }
    }

    // Null when there is nothing to refresh: not started yet (Start will pre-fill it from this report), or submitted
    // (frozen).
    private async Task<MaturityAssessmentDetailRow?> ReadOpenAssessmentAsync(Collection collection, Organisation organisation)
    {
        var lookup = await assessmentQueries.GetDetailForCollectionAndOrgAsync(collection.Id, organisation.Id);
        if (lookup.IsNone)
        {
            return null;
        }

        var assessment = lookup.ValueUnsafe();
        return assessment.SubmittedAt is null ? assessment : null;
    }
}