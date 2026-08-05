using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Submissions.Exceptions;
using Application.Submissions.Protocol;
using Domain.Activities;
using Domain.Collections;
using Domain.DataCollectionForms;
using Domain.Organisations;
using Domain.Submissions;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Application.Submissions;

// The resolved subject of an "Enter activity key results" save or complete: the eligible submission, its collection,
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
    ISubmissionQueries submissionQueries,
    ISubmissionActivityReportRepository reportRepository,
    ICollectionQueries collectionQueries,
    IOrganisationQueries organisationQueries,
    IActivityQueries activityQueries,
    IActivityExpectedResultRepository expectedResultRepository,
    IKeyResultQueries keyResultQueries,
    IFormModuleQueries formModuleQueries,
    PermissionsService permissionsService)
{
    public async Task<Either<SubmissionException, ActivityReportContext>> ResolveAsync(
        Guid collectionId,
        Guid activityId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<Either<SubmissionException, ActivityReportContext>>(
                permission => ResolveWithPermission(collectionId, activityId, permission, cancellationToken),
                error => new SubmissionForbiddenException(SubmissionId.Empty(), error.Message));
    }

    // Reportable = completed setup with a status of active, closed or finished (Planned and draft records are
    // excluded — the quarterly report only asks for updates on work that has started). Deliberately stricter than
    // FundingSpendContextResolver.IsFundingReportable.
    public static bool IsReportable(Activity activity) =>
        activity.IsComplete
        && !activity.IsDeleted
        && activity.Status is not null
        && !activity.Status.Equals(ActivityStatus.Planned);

    // The activity's resolved question set: protocol questions for its selected key results and status phrasing,
    // a generic progress question per custom result, and the module's delivery questions.
    public async Task<IReadOnlyList<ResolvedQuestion>> GetResolvedQuestionsAsync(
        Activity activity,
        CancellationToken cancellationToken)
    {
        var expectedResults = await expectedResultRepository.GetForActivityAsync(activity.Id, cancellationToken);
        var questionBank = activity.TaskId is null
            ? []
            : await keyResultQueries.GetQuestionsForTaskAsync(activity.TaskId, cancellationToken);
        var deliveryQuestions = await GetDeliveryQuestionsAsync(cancellationToken);

        return KeyResultQuestionResolver.Resolve(activity, expectedResults, questionBank, deliveryQuestions);
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
                SubmissionActivityReportId.New(), context.Submission.Id, context.Activity.Id, context.ActorId);
            if (completed)
            {
                report.MarkCompleted();
            }

            return await reportRepository.AddAsync(report, cancellationToken);
        }

        var current = existing.ValueUnsafe();
        if (completed)
        {
            current.MarkCompleted();
        }
        else
        {
            current.MarkInProgress();
        }

        await reportRepository.UpdateAsync(current, cancellationToken);
        return current;
    }

    // Section status roll-up: Completed once every reportable activity has a Completed report row; In progress when
    // any report row exists; otherwise Not started. Stored on the submission section so the task list is one read.
    public async Task RecomputeSectionStatusAsync(ActivityReportContext context, CancellationToken cancellationToken)
    {
        // Sections are snapshotted when the report is started; if this collection was never assigned the
        // key-results module there is no section row to roll up — skip rather than fault the save.
        if (context.Submission.Sections.All(s => s.ModuleCode != SubmissionModules.ActivityKeyResults))
        {
            return;
        }

        var allActivities = await activityQueries.GetByOrganisationAsync(context.OrganisationId, status: null, cancellationToken);
        var reportableIds = allActivities.Where(IsReportable).Select(a => a.Id).ToHashSet();
        var reports = await reportRepository.GetForSubmissionAsync(context.Submission.Id, cancellationToken);

        var completedIds = reports
            .Where(r => r.Status.IsCompleted)
            .Select(r => r.ActivityId)
            .ToHashSet();

        var status = reportableIds.Count > 0 && reportableIds.All(completedIds.Contains)
            ? SubmissionSectionStatus.Completed
            : reports.Count > 0
                ? SubmissionSectionStatus.InProgress
                : SubmissionSectionStatus.NotStarted;

        context.Submission.SetSectionStatus(SubmissionModules.ActivityKeyResults, status, context.ActorId);
        await submissionRepository.UpdateAsync(context.Submission, cancellationToken);
    }

    private async Task<IReadOnlyList<DcFormQuestion>> GetDeliveryQuestionsAsync(CancellationToken cancellationToken)
    {
        var moduleLookup = await formModuleQueries.GetByCodeAsync(SubmissionModules.ActivityKeyResults, cancellationToken);
        if (moduleLookup.IsNone)
        {
            return [];
        }

        var withChildren = await formModuleQueries.GetByIdWithChildrenAsync(moduleLookup.ValueUnsafe().Id, cancellationToken);
        return withChildren.IsNone ? [] : withChildren.ValueUnsafe().Questions.ToList();
    }

    private async Task<Either<SubmissionException, ActivityReportContext>> ResolveWithPermission(
        Guid collectionId,
        Guid activityId,
        Permission permission,
        CancellationToken cancellationToken)
    {
        if (permission.OrganisationId.IsNone || permission.UserId.IsNone)
        {
            return new SubmissionForbiddenException(SubmissionId.Empty(), "You are not associated with an organisation.");
        }

        var organisationId = permission.OrganisationId.ValueUnsafe();
        var actorId = permission.UserId.ValueUnsafe();
        var collectionRef = new CollectionId(collectionId);
        var activityRef = new ActivityId(activityId);

        var collectionLookup = await collectionQueries.GetByIdAsync(collectionRef, cancellationToken: cancellationToken);
        if (collectionLookup.IsNone)
        {
            return new SubmissionCollectionNotEligibleException(collectionRef);
        }

        var organisationLookup = await organisationQueries.GetOrganisationById(organisationId, cancellationToken);
        if (organisationLookup.IsNone)
        {
            return new SubmissionCollectionNotEligibleException(collectionRef);
        }

        var organisation = organisationLookup.ValueUnsafe();
        var collection = collectionLookup.ValueUnsafe();
        if (!SubmissionEligibility.IsQuarterly(collection) || !SubmissionEligibility.IsAssignedTo(collection, organisation))
        {
            return new SubmissionCollectionNotEligibleException(collectionRef);
        }

        if (!SubmissionEligibility.IsOpen(collection))
        {
            return new SubmissionCollectionNotOpenException(collectionRef, collection.GetStatus());
        }

        var submissionLookup = await submissionQueries.GetByCollectionAndOrgAsync(collectionRef, organisationId, cancellationToken);
        if (submissionLookup.IsNone)
        {
            return new SubmissionDoesNotExistException(collectionRef);
        }

        var submission = submissionLookup.ValueUnsafe();
        if (submission.IsSubmitted)
        {
            return new SubmissionAlreadySubmittedException(submission.Id);
        }

        if (!submission.AreActivitiesConfirmed)
        {
            return new SubmissionActivitiesNotConfirmedException(submission.Id);
        }

        var activityLookup = await activityQueries.GetByIdForOrgAsync(activityRef, organisationId, cancellationToken: cancellationToken);
        if (activityLookup.IsNone)
        {
            return new SubmissionActivityNotReportableException(submission.Id, activityRef, "it does not exist.");
        }

        var activity = activityLookup.ValueUnsafe();
        if (!IsReportable(activity))
        {
            return new SubmissionActivityNotReportableException(
                submission.Id, activityRef, "only completed activities that are active, closed or finished can be reported on.");
        }

        return new ActivityReportContext(submission, collection, organisationId, actorId, activity);
    }
}