using Application.Common.Features;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Submissions.Protocol;
using Domain.Activities;
using Domain.Collections;
using Domain.Organisations;
using Domain.Submissions;
using Domain.Users;
using LanguageExt.UnsafeValueAccess;
using Microsoft.Extensions.Logging;

namespace Application.Submissions;

// Seeds a quarterly report's activity answers with the ones the organisation last submitted for the same activity
// (BLUM-7671). A FINISHED activity's results cannot change again, so re-keying them every quarter is pure toil.
//
// Deliberately a WRITE at confirm-activities rather than an overlay on the read: the LA must own these rows.
// A read-time merge cannot be cleared (a blank is never persisted — every answer validator drops empty values —
// so deleting a carried value removes the row and the next GET re-injects it), and it leaks into finalised
// history, because ReportSnapshotService freezes a submitted report through the very same read.
//
// Called by ConfirmActivitiesCommandHandler, which already enforces quarterly + assigned + open window +
// not-submitted. Like SubmissionReviewService, this owns its own try/catch: a failure to carry answers forward
// must never stop the LA confirming their activity list — they simply re-key, which is today's behaviour. That is
// why both callers run it only after their own transaction has committed (BLUM-8748): a failed statement aborts the
// PostgreSQL transaction it runs in, so a swallowed database failure inside one would still roll the caller back.
public class ActivityResultCarryForward(
    IActivityQueries activityQueries,
    ActivityReportContextResolver resolver,
    ICollectionQueries collectionQueries,
    ISubmissionQueries submissionQueries,
    ISubmissionActivityAnswerRepository answerRepository,
    ISubmissionActivityReportRepository reportRepository,
    IDateTimeProvider dateTimeProvider,
    IFeatureFlags featureFlags,
    ILogger<ActivityResultCarryForward> logger)
{
    public async Task CopyForFinishedActivitiesAsync(
        Submission submission,
        Collection collection,
        OrganisationId organisationId,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Every published collection derives a reporting period (ReportingPeriod.Derive); a draft has none —
            // and no submissions to look back from either.
            if (collection.ReportingPeriodEnd is not { } reportingPeriodEnd)
            {
                return;
            }

            // Sections are snapshotted when the report is started, so a quarterly collection published without the
            // key-results module has no section to show these answers in. Writing them anyway would leave rows the
            // LA can never see or clear but that the report snapshot would still export. Same guard, same reason,
            // as ActivityReportContextResolver.RecomputeSectionStatusAsync.
            if (submission.Sections.All(s => s.ModuleCode != SubmissionModules.ActivityKeyResults))
            {
                return;
            }

            var activities = await activityQueries.GetByOrganisationAsync(organisationId, status: null, cancellationToken);

            foreach (var activity in activities.Where(a =>
                ActivityReportContextResolver.IsReportable(a)
                && KeyResultQuestionResolver.CarriesForwardPreviousAnswers(a.Status)))
            {
                await CopyForActivityAsync(submission, collection, activity, organisationId, actorId, reportingPeriodEnd, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to carry finished activity results forward into submission {SubmissionId} for organisation {OrganisationId}.",
                submission.Id,
                organisationId);
        }
    }

    // The second moment the copy has to run, for an ordering confirm-time cannot see. Two quarterly windows can be
    // open at once (BLUM-6950), so an LA can confirm Q2's activity list BEFORE finalising Q1. At that point Q1 is
    // not yet "previously submitted data", so nothing carries — and confirming is a one-shot: submitting Q1 changes
    // no activity, so SubmissionReviewService raises no review flag and nothing would ever ask the LA to reconfirm
    // Q2. They would simply find Q2 blank and re-key it, which is the toil the ticket exists to remove. Finalising
    // a report is the only other event that changes the answer to "what has this organisation submitted before?".
    // Best-effort like everything else here: SubmitReport runs this only after the submit has committed.
    public async Task CopyIntoOtherOpenSubmissionsAsync(
        Organisation organisation,
        CollectionId submittedCollectionId,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        try
        {
            var collections = await collectionQueries.GetAssignedToOrganisationAsync(organisation.Id, cancellationToken);
            var utcNow = dateTimeProvider.GetUtcNow();

            foreach (var collection in collections.Where(c =>
                c.Id != submittedCollectionId
                && SubmissionEligibility.IsQuarterly(c)
                && SubmissionEligibility.IsOpen(c, utcNow)
                && SubmissionEligibility.IsAssignedTo(c, organisation)))
            {
                var submissionLookup = await submissionQueries.GetByCollectionAndOrgAsync(
                    collection.Id, organisation.Id, cancellationToken);
                if (submissionLookup.IsNone)
                {
                    continue;
                }

                // An unconfirmed report is left alone: the LA has not reached the step yet, and confirming is
                // what will seed it. A submitted one is closed.
                var submission = submissionLookup.ValueUnsafe();
                if (submission.IsSubmitted || !submission.AreActivitiesConfirmed)
                {
                    continue;
                }

                await CopyForFinishedActivitiesAsync(submission, collection, organisation.Id, actorId, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to carry finished activity results into the other open submissions of organisation {OrganisationId}.",
                organisation.Id);
        }
    }

    private async Task CopyForActivityAsync(
        Submission submission,
        Collection collection,
        Activity activity,
        OrganisationId organisationId,
        UserId actorId,
        DateOnly reportingPeriodEnd,
        CancellationToken cancellationToken)
    {
        // Two guards, neither redundant. Confirm-activities has no already-confirmed gate and re-confirming is
        // routine (the task is always clickable, and every activity-list edit re-raises the review flag), so this
        // runs many times per submission and must be a no-op after the first:
        //   - the report row is the one that carries meaning. Every save creates one, so its presence means the LA
        //     has opened the section — including the case where they cleared every carried field, which must not
        //     be resurrected or this reintroduces the un-clearable value the whole change exists to fix;
        //   - the answer rows stop a repeat confirm rewriting rows the LA already has. ReplaceForActivityAsync
        //     deletes-then-inserts, so without this the copy would be correct but destructive: same values, new
        //     row ids, and CreatedBy/CreatedAt re-stamped on every confirm — the only provenance these rows have.
        var existingReport = await reportRepository.GetForActivityAsync(submission.Id, activity.Id, cancellationToken);
        if (existingReport.IsSome)
        {
            return;
        }

        var existingAnswers = await answerRepository.GetForActivityAsync(submission.Id, activity.Id, cancellationToken);
        if (existingAnswers.Count > 0)
        {
            return;
        }

        var priorAnswers = await submissionQueries.GetPriorSubmittedActivityAnswersAsync(
            organisationId, activity.Id, reportingPeriodEnd, cancellationToken);
        if (priorAnswers.Count == 0)
        {
            return;
        }

        var resolved = await resolver.GetResolvedQuestionsAsync(collection, activity, cancellationToken);
        var carried = KeyResultQuestionResolver.BuildCarryForward(resolved, priorAnswers);
        if (carried.Count == 0)
        {
            return;
        }

        // These are the only rows written to submission_activity_answers that do not come from a page save, so they
        // run the same validator that one does. BuildCarryForward only checks the CODE still resolves — the VALUE
        // can have gone stale, because reseeding the key-result protocol between quarters can rename or drop an
        // option (BLUM-7472, 7475 and 7563 all did). Persisting one unchecked would show the LA a value they never
        // chose and then block Check-your-answers with an error on a field they never touched. Drop those first;
        // CollectPersistable then removes anything a dropped parent has hidden, exactly as the save path does.
        var candidates = new Dictionary<string, string?>(carried, StringComparer.Ordinal);
        var transitive = featureFlags.IsEnabled(Feature.BLUM_9087);
        foreach (var error in SubmissionAnswerValidator.Validate(resolved, candidates, enforceRequired: false, transitive))
        {
            candidates.Remove(error.QuestionCode);
        }

        var persistable = SubmissionAnswerValidator.CollectPersistable(resolved, candidates, transitive);
        if (persistable.Count == 0)
        {
            return;
        }

        // SubmissionActivityAnswer has no provenance column, so a carried row is stamped with the confirming user
        // — the same CreatedBy the LA would get by typing the value in themselves, which is what these rows are.
        var rows = persistable
            .Select(answer => SubmissionActivityAnswer.Record(
                SubmissionActivityAnswerId.New(),
                submission.Id,
                activity.Id,
                answer.Question.Code,
                answer.Value,
                actorId,
                dateTimeProvider.GetUtcNow()))
            .ToList();

        // Delete-then-insert; the guards above have already established there is nothing to delete.
        await answerRepository.ReplaceForActivityAsync(submission.Id, activity.Id, rows, cancellationToken);

        // Deliberately NO SubmissionActivityReport row: RecomputeSectionStatusAsync derives section status purely
        // from report rows, so creating one would flip "Enter activity results" to In progress at confirm time —
        // the LA would see a section they have not opened claiming progress. Leaving it absent keeps the task list
        // honest: they still walk the pages, see the values pre-filled, and Continue.
    }
}