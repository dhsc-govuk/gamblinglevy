using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Submissions.Exceptions;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Organisations;
using Domain.Submissions;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Application.Submissions;

// The resolved subject of a "Provide strategic feedback" save: the eligible submission, its collection and the acting
// organisation/user. The eligibility checks are QuarterlyReportWriteGate's, shared with every other quarterly-report
// write. Feedback has no per-activity fan-out, so there are no reportable activities to resolve.
public record StrategicFeedbackContext(
    Submission Submission,
    Collection Collection,
    OrganisationId OrganisationId,
    UserId ActorId);

public class StrategicFeedbackContextResolver(
    ISubmissionRepository submissionRepository,
    ISubmissionFeedbackAnswerRepository feedbackRepository,
    CollectionFormModules collectionFormModules,
    QuarterlyReportWriteGate writeGate,
    IDateTimeProvider dateTimeProvider)
{
    public async Task<Either<SubmissionException, StrategicFeedbackContext>> ResolveAsync(Guid collectionId, CancellationToken cancellationToken)
    {
        var gate = await writeGate.ResolveAsync(collectionId, cancellationToken);
        return gate.Map(For);
    }

    // The feedback context for a report that has already passed QuarterlyReportWriteGate.
    public static StrategicFeedbackContext For(QuarterlyReportWriteContext gate) =>
        new(gate.Submission, gate.Collection, gate.OrganisationId, gate.ActorId);

    // The active feedback questions in display order. Empty when the module isn't seeded/published yet — in which case
    // there are no required answers to enforce and the section completes trivially. They are the collection's version
    // of the module (BLUM-9104), so a report is checked against the questions it was given.
    public async Task<IReadOnlyList<DcFormQuestion>> GetQuestionsAsync(Collection collection, CancellationToken cancellationToken)
    {
        var module = await collectionFormModules.GetAsync(collection, SubmissionModules.StrategicFeedback, cancellationToken);
        return module.IsNone
            ? []
            : module.ValueUnsafe().Questions.Where(q => q.IsActive).OrderBy(q => q.DisplayOrder).ToList();
    }

    // Rolls the "Provide strategic feedback" section to the given status and persists it. No-op when the collection was
    // never assigned the feedback module (no section row to roll up) — mirrors FundingSpend.SetSectionStatusAsync.
    public async Task SetSectionStatusAsync(StrategicFeedbackContext context, SubmissionSectionStatus status, CancellationToken cancellationToken)
    {
        if (context.Submission.Sections.All(s => s.ModuleCode != SubmissionModules.StrategicFeedback))
        {
            return;
        }

        context.Submission.SetSectionStatus(SubmissionModules.StrategicFeedback, status, context.ActorId, dateTimeProvider.GetUtcNow());
        await submissionRepository.UpdateAsync(context.Submission, cancellationToken);
    }

    // After a draft save: In progress when any answer is saved, otherwise Not started. Never Completed — only the
    // "Check your answers" Continue (CompleteStrategicFeedbackCommand) completes the section. Re-saving a draft
    // therefore drops a previously Completed section back to In progress.
    public async Task<SubmissionSectionStatus> RecomputeDraftStatusAsync(StrategicFeedbackContext context, CancellationToken cancellationToken)
    {
        var answers = await feedbackRepository.GetForSubmissionAsync(context.Submission.Id, cancellationToken);
        var status = answers.Count > 0 ? SubmissionSectionStatus.InProgress : SubmissionSectionStatus.NotStarted;
        await SetSectionStatusAsync(context, status, cancellationToken);
        return status;
    }
}