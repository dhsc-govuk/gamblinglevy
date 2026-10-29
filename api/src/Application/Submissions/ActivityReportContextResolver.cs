using Application.Common.Features;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Submissions.Exceptions;
using Application.Submissions.Protocol;
using Domain.Activities;
using Domain.Categories;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Organisations;
using Domain.Submissions;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Application.Submissions;

// The resolved subject of an "Enter activity results" save or complete: the eligible submission, its collection,
// the acting organisation/user and the reportable activity. Mirrors the FundingSpend/StrategicFeedback resolvers so
// the save and complete commands share one gate, one question resolution and one section roll-up.
public record ActivityReportContext(
    Submission Submission,
    Collection Collection,
    OrganisationId OrganisationId,
    UserId ActorId,
    Activity Activity);

public class ActivityReportContextResolver(
    ISubmissionRepository submissionRepository,
    ISubmissionActivityReportRepository reportRepository,
    ISubmissionActivityAnswerRepository answerRepository,
    IActivityQueries activityQueries,
    IActivityExpectedResultRepository expectedResultRepository,
    IKeyResultQueries keyResultQueries,
    CollectionFormModules collectionFormModules,
    ICategoryQueries categoryQueries,
    QuarterlyReportWriteGate writeGate,
    IDateTimeProvider dateTimeProvider,
    IFeatureFlags featureFlags)
{
    public async Task<Either<SubmissionException, ActivityReportContext>> ResolveAsync(
        Guid collectionId,
        Guid activityId,
        CancellationToken cancellationToken)
    {
        var gate = await writeGate.ResolveAsync(collectionId, cancellationToken);
        return await gate.MatchAsync<Either<SubmissionException, ActivityReportContext>>(
            context => ForActivityAsync(context, new ActivityId(activityId), cancellationToken),
            error => error);
    }

    // The context for one activity of a report that has already passed QuarterlyReportWriteGate, when the caller
    // has already established the activity is reportable (the offline upload loads every reportable activity once).
    public static ActivityReportContext For(QuarterlyReportWriteContext gate, Activity activity) =>
        new(gate.Submission, gate.Collection, gate.OrganisationId, gate.ActorId, activity);

    // Every activity of the organisation this section asks about, in the order the activity list shows them.
    public async Task<IReadOnlyList<Activity>> GetReportableActivitiesAsync(
        OrganisationId organisationId,
        CancellationToken cancellationToken)
    {
        var activities = await activityQueries.GetByOrganisationAsync(organisationId, status: null, cancellationToken);
        return activities.Where(IsReportable).ToList();
    }

    // Reportable = completed setup with a status of active, closed or finished (Planned and draft records are
    // excluded — the quarterly report only asks for updates on work that has started).
    // FundingSpendContextResolver.IsActualSpendReportable applies the same Planned exclusion to actual spend for the
    // same reason; Planned activities appear only on the funding section's forecast (BLUM-7672).
    //
    // Every category reports here, Joint partnership included. BLUM-7860 kept partnerships out of this section (they
    // had no measures, and asked their progress questions on the record instead); BLUM-8090 moved those questions
    // into the report, so a partnership now answers the "Activity delivery" page like any other activity — in its
    // own wording, see IsJointPartnershipAsync — and must be completed before the report can be submitted.
    public static bool IsReportable(Activity activity) =>
        activity.IsComplete
        && !activity.IsDeleted
        && activity.Status is not null
        && !activity.Status.Equals(ActivityStatus.Planned);

    // Whether the activity is a Joint partnership, whose delivery questions are asked in the partnership wording
    // (JointPartnershipDeliveryCopy). Resolved by name because category ids are minted per environment (see
    // CategoryNames).
    //
    // Static so QuarterlyReportControllerService — which owns the read side of the same module and already injects
    // ICategoryQueries — words the questions identically without taking a dependency on this resolver.
    public static async Task<bool> IsJointPartnershipAsync(
        ICategoryQueries categoryQueries,
        Activity activity,
        CancellationToken cancellationToken)
    {
        if (activity.CategoryId is null)
        {
            return false;
        }

        var categories = await categoryQueries.GetActiveCategoriesAsync(cancellationToken);
        return IsJointPartnership(categories, activity);
    }

    // The same test against categories the caller has already loaded, for a caller resolving many activities at once.
    public static bool IsJointPartnership(IReadOnlyList<Category> categories, Activity activity) =>
        activity.CategoryId is not null
        && categories.Any(category => category.Id == activity.CategoryId && category.Name == CategoryNames.JointPartnership);

    // The activity's resolved question set: protocol questions for its selected key results and status phrasing,
    // a generic progress question per custom result, and the module's delivery questions — as the collection's version
    // holds them (BLUM-9104).
    public async Task<IReadOnlyList<ResolvedQuestion>> GetResolvedQuestionsAsync(
        Collection collection,
        Activity activity,
        CancellationToken cancellationToken)
    {
        var expectedResults = await expectedResultRepository.GetForActivityAsync(activity.Id, cancellationToken);
        var questionBank = activity.TaskId is null
            ? []
            : await keyResultQueries.GetQuestionsForTaskAsync(activity.TaskId, cancellationToken);
        var deliveryQuestions = await LoadDeliveryQuestionsAsync(collectionFormModules, collection, cancellationToken);
        var isJointPartnership = await IsJointPartnershipAsync(categoryQueries, activity, cancellationToken);

        return KeyResultQuestionResolver.Resolve(activity, expectedResults, questionBank, deliveryQuestions, isJointPartnership);
    }

    // Persists an activity's answers as its completed report: the visible, non-blank answers replace everything saved
    // for the activity (pruning hidden and orphaned codes) and the report row is marked Completed. The caller has
    // already validated the answers with enforceRequired: true, and recomputes the section roll-up afterwards — once,
    // when it completes several activities together.
    public async Task<SubmissionActivityReport> WriteCompletedReportAsync(
        ActivityReportContext context,
        IReadOnlyList<ResolvedQuestion> questions,
        IReadOnlyDictionary<string, string?> answers,
        CancellationToken cancellationToken)
    {
        var persistable = SubmissionAnswerValidator.CollectPersistable(questions, answers, featureFlags.IsEnabled(Feature.BLUM_9087))
            .Select(p => SubmissionActivityAnswer.Record(
                SubmissionActivityAnswerId.New(), context.Submission.Id, context.Activity.Id, p.Question.Code, p.Value, context.ActorId, dateTimeProvider.GetUtcNow()))
            .ToList();

        await answerRepository.ReplaceForActivityAsync(context.Submission.Id, context.Activity.Id, persistable, cancellationToken);
        return await UpsertReportAsync(context, completed: true, cancellationToken);
    }

    public async Task<SubmissionActivityReport> UpsertReportAsync(
        ActivityReportContext context,
        bool completed,
        CancellationToken cancellationToken)
    {
        var existing = await reportRepository.GetForActivityAsync(context.Submission.Id, context.Activity.Id, cancellationToken);

        if (existing.IsNone)
        {
            var report = SubmissionActivityReport.Start(
                SubmissionActivityReportId.New(), context.Submission.Id, context.Activity.Id, context.ActorId, dateTimeProvider.GetUtcNow());
            if (completed)
            {
                report.MarkCompleted(dateTimeProvider.GetUtcNow());
            }

            return await reportRepository.AddAsync(report, cancellationToken);
        }

        var current = existing.ValueUnsafe();
        if (completed)
        {
            current.MarkCompleted(dateTimeProvider.GetUtcNow());
        }
        else
        {
            current.MarkInProgress(dateTimeProvider.GetUtcNow());
        }

        await reportRepository.UpdateAsync(current, cancellationToken);
        return current;
    }

    // Section status roll-up: Completed once every reportable activity has a Completed report row; In progress when
    // any report row exists; otherwise Not started. Stored on the submission section so the task list is one read.
    public Task RecomputeSectionStatusAsync(ActivityReportContext context, CancellationToken cancellationToken) =>
        RecomputeSectionStatusAsync(context.Submission, context.OrganisationId, context.ActorId, cancellationToken);

    // The same roll-up without an activity in hand. ConfirmActivitiesCommand needs it and has none: it is the one
    // caller that runs when the reportable set may be EMPTY (BLUM-7860). This method is the only writer of the
    // key-results section status, and the two commands that otherwise reach it both require a reportable activity
    // id — so an organisation with nothing to report here could never write a status at all, leaving it Not started
    // forever and blocking SubmitReportCommand's AreAllSectionsCompleted gate. BLUM-8090 made Joint partnerships
    // reportable, so that is now only an organisation whose activities are all Planned or drafts, or which has none.
    public async Task RecomputeSectionStatusAsync(
        Submission submission,
        OrganisationId organisationId,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        // Sections are snapshotted when the report is started; if this collection was never assigned the
        // key-results module there is no section row to roll up — skip rather than fault the save.
        if (submission.Sections.All(s => s.ModuleCode != SubmissionModules.ActivityKeyResults))
        {
            return;
        }

        var allActivities = await activityQueries.GetByOrganisationAsync(organisationId, status: null, cancellationToken);
        var reportableIds = allActivities.Where(IsReportable).Select(a => a.Id).ToHashSet();
        var reports = await reportRepository.GetForSubmissionAsync(submission.Id, cancellationToken);

        var completedIds = reports
            .Where(r => r.Status.IsCompleted)
            .Select(r => r.ActivityId)
            .ToHashSet();

        // "Every activity that needs a report has one" — vacuously true when nothing needs one. The previous `reportableIds.Count > 0 &&` guard made an empty set Not
        // started, a dead end for an organisation with no reportable activities at all.
        var status = reportableIds.All(completedIds.Contains)
            ? SubmissionSectionStatus.Completed
            : reports.Count > 0
                ? SubmissionSectionStatus.InProgress
                : SubmissionSectionStatus.NotStarted;

        submission.SetSectionStatus(SubmissionModules.ActivityKeyResults, status, actorId, dateTimeProvider.GetUtcNow());
        await submissionRepository.UpdateAsync(submission, cancellationToken);
    }

    // Every "Activity delivery" question of the activity_key_results module, retired ones included — Resolve keeps
    // only the active ones, and the activity-record export (BLUM-7648) still needs a retired one to label an answer
    // an older report gave. Static for the same reason as IsJointPartnershipAsync. With a collection, the questions are
    // that collection's version (BLUM-9104); with none (the activity-record export spans many reports), the live ones.
    public static async Task<IReadOnlyList<DcFormQuestion>> LoadDeliveryQuestionsAsync(
        CollectionFormModules collectionFormModules,
        Collection? collection,
        CancellationToken cancellationToken)
    {
        var module = await collectionFormModules.GetAsync(collection, SubmissionModules.ActivityKeyResults, cancellationToken);
        return module.IsNone ? [] : module.ValueUnsafe().Questions.ToList();
    }

    private async Task<Either<SubmissionException, ActivityReportContext>> ForActivityAsync(
        QuarterlyReportWriteContext gate,
        ActivityId activityRef,
        CancellationToken cancellationToken)
    {
        var activityLookup = await activityQueries.GetByIdForOrgAsync(activityRef, gate.OrganisationId, cancellationToken: cancellationToken);
        if (activityLookup.IsNone)
        {
            return new SubmissionActivityNotReportableException(gate.Submission.Id, activityRef, "it does not exist.");
        }

        var activity = activityLookup.ValueUnsafe();
        if (!IsReportable(activity))
        {
            return new SubmissionActivityNotReportableException(
                gate.Submission.Id,
                activityRef,
                "only completed activities that are active, closed or finished can be reported on.");
        }

        return For(gate, activity);
    }
}