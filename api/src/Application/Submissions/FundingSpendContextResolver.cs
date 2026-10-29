using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Submissions.Exceptions;
using Domain.Activities;
using Domain.Collections;
using Domain.Organisations;
using Domain.Submissions;
using Domain.Users;
using LanguageExt;

namespace Application.Submissions;

// The resolved subject of a "Record funding spent" save: the eligible submission, its collection, the acting
// organisation/user and the activities each screen renders. The eligibility checks are QuarterlyReportWriteGate's,
// shared with every other quarterly-report write.
//
// Two activity sets, one per screen (BLUM-7672): "Activity spending" takes actuals from ActualSpendActivities, and
// "Forecast spend" takes forecasts from ForecastSpendActivities, which is the same set plus Planned. Every consumer
// has to pick the one for the screen it serves — a single shared list is what let Planned onto the actuals screen.
public record FundingSpendContext(
    Submission Submission,
    Collection Collection,
    OrganisationId OrganisationId,
    UserId ActorId,
    IReadOnlyList<Activity> ActualSpendActivities,
    // The same activities named the way the screens label their rows, for the protocol's per-activity money
    // messages. Resolved here because the type name that backs the fallback needs a second query.
    IReadOnlyList<FundingSpendSubject> ActualSpendSubjects,
    IReadOnlyList<Activity> ForecastSpendActivities,
    IReadOnlyList<FundingSpendSubject> ForecastSpendSubjects);

public class FundingSpendContextResolver(
    ISubmissionRepository submissionRepository,
    ISubmissionSpendEntryRepository spendRepository,
    IActivityQueries activityQueries,
    ICategoryQueries categoryQueries,
    QuarterlyReportWriteGate writeGate,
    IDateTimeProvider dateTimeProvider)
{
    public async Task<Either<SubmissionException, FundingSpendContext>> ResolveAsync(Guid collectionId, CancellationToken cancellationToken)
    {
        var gate = await writeGate.ResolveAsync(collectionId, cancellationToken);
        return await gate.MatchAsync<Either<SubmissionException, FundingSpendContext>>(
            async context => await ForAsync(context, cancellationToken),
            error => error);
    }

    // The funding context for a report that has already passed QuarterlyReportWriteGate.
    public async Task<FundingSpendContext> ForAsync(QuarterlyReportWriteContext gate, CancellationToken cancellationToken)
    {
        var activities = await activityQueries.GetByOrganisationAsync(gate.OrganisationId, status: null, cancellationToken);
        var actualSpend = activities.Where(IsActualSpendReportable).ToList();
        var forecastSpend = activities.Where(IsForecastSpendReportable).ToList();

        var taskNames = (await categoryQueries.GetActiveTasksAsync(cancellationToken))
            .ToDictionary(task => task.Id, task => task.TaskName);
        IReadOnlyList<FundingSpendSubject> SubjectsFor(IEnumerable<Activity> set) => set
            .Select(activity => FundingSpendSubject.For(
                activity,
                activity.TaskId is not null && taskNames.TryGetValue(activity.TaskId, out var taskName) ? taskName : null))
            .ToList();

        return new FundingSpendContext(
            gate.Submission,
            gate.Collection,
            gate.OrganisationId,
            gate.ActorId,
            actualSpend,
            SubjectsFor(actualSpend),
            forecastSpend,
            SubjectsFor(forecastSpend));
    }

    // Takes ACTUAL spend on "Activity spending": completed setup, not deleted, with a status of active, closed or
    // finished. Planned is excluded, matching ActivityReportContextResolver.IsReportable: a merely-planned activity
    // has not started, so it cannot yet have incurred real spend, and letting it onto that screen let LAs record an
    // "actual" against work that had not happened, which then pulled straight into the DHSC-facing report (BLUM-7504).
    public static bool IsActualSpendReportable(Activity activity) =>
        IsForecastSpendReportable(activity)
        && !activity.Status!.Equals(ActivityStatus.Planned);

    // Takes FORECAST spend on "Forecast spend": everything above, plus Planned. Planned work has spent nothing yet
    // but is exactly what an LA expects to spend on, so DHSC wants it in the forecast (BLUM-7672). The lineage is
    // worth knowing before changing either predicate: BLUM-6612 let Planned onto both screens, BLUM-7504 took it off
    // both, and BLUM-7672 put it back on the forecast only.
    public static bool IsForecastSpendReportable(Activity activity) =>
        activity.IsComplete
        && !activity.IsDeleted
        && activity.Status is not null;

    // Rolls the "Record funding spent" section to the given status and persists it. No-op when the collection was
    // never assigned the funding module (no section row to roll up) — mirrors RecomputeSectionStatusAsync.
    public async Task SetSectionStatusAsync(FundingSpendContext context, SubmissionSectionStatus status, CancellationToken cancellationToken)
    {
        if (context.Submission.Sections.All(s => s.ModuleCode != SubmissionModules.FundingSpent))
        {
            return;
        }

        context.Submission.SetSectionStatus(SubmissionModules.FundingSpent, status, context.ActorId, dateTimeProvider.GetUtcNow());
        await submissionRepository.UpdateAsync(context.Submission, cancellationToken);
    }

    // After a draft save: In progress when any spend (of either kind) is saved, otherwise Not started. Never
    // Completed — only the "Check your spending totals" Continue (CompleteFundingSpentCommand) completes the
    // section. Re-saving a draft therefore drops a previously Completed section back to In progress.
    public async Task<SubmissionSectionStatus> RecomputeDraftStatusAsync(FundingSpendContext context, CancellationToken cancellationToken)
    {
        var entries = await spendRepository.GetForSubmissionAsync(context.Submission.Id, cancellationToken);
        var status = entries.Count > 0 ? SubmissionSectionStatus.InProgress : SubmissionSectionStatus.NotStarted;
        await SetSectionStatusAsync(context, status, cancellationToken);
        return status;
    }
}