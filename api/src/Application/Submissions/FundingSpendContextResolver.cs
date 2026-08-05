using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Submissions.Exceptions;
using Domain.Activities;
using Domain.Collections;
using Domain.Organisations;
using Domain.Submissions;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Application.Submissions;

// The resolved subject of a "Record funding spent" save: the eligible submission, its collection, the acting
// organisation/user and the reportable activities the screens render. Mirrors the SaveActivityReport eligibility
// preamble so the three funding commands share one gate.
public record FundingSpendContext(
    Submission Submission,
    Collection Collection,
    OrganisationId OrganisationId,
    UserId ActorId,
    IReadOnlyList<Activity> ReportableActivities);

public class FundingSpendContextResolver(
    ISubmissionRepository submissionRepository,
    ISubmissionQueries submissionQueries,
    ISubmissionSpendEntryRepository spendRepository,
    ICollectionQueries collectionQueries,
    IOrganisationQueries organisationQueries,
    IActivityQueries activityQueries,
    PermissionsService permissionsService)
{
    public async Task<Either<SubmissionException, FundingSpendContext>> ResolveAsync(Guid collectionId, CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<Either<SubmissionException, FundingSpendContext>>(
                permission => ResolveWithPermission(collectionId, permission, cancellationToken),
                error => new SubmissionForbiddenException(SubmissionId.Empty(), error.Message));
    }

    // Funding-reportable = completed setup, not deleted, with a chosen lifecycle status — INCLUDING Planned. LAs
    // record and forecast levy spend for planned work too (BLUM-6612 / designs), so this is deliberately wider than
    // SaveActivityReportCommandHandler.IsReportable, which excludes Planned from the activity key-results report.
    public static bool IsFundingReportable(Activity activity) =>
        activity.IsComplete && !activity.IsDeleted && activity.Status is not null;

    // Rolls the "Record funding spent" section to the given status and persists it. No-op when the collection was
    // never assigned the funding module (no section row to roll up) — mirrors RecomputeSectionStatusAsync.
    public async Task SetSectionStatusAsync(FundingSpendContext context, SubmissionSectionStatus status, CancellationToken cancellationToken)
    {
        if (context.Submission.Sections.All(s => s.ModuleCode != SubmissionModules.FundingSpent))
        {
            return;
        }

        context.Submission.SetSectionStatus(SubmissionModules.FundingSpent, status, context.ActorId);
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

    private async Task<Either<SubmissionException, FundingSpendContext>> ResolveWithPermission(
        Guid collectionId,
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

        var activities = await activityQueries.GetByOrganisationAsync(organisationId, status: null, cancellationToken);
        var reportable = activities.Where(IsFundingReportable).ToList();

        return new FundingSpendContext(submission, collection, organisationId, actorId, reportable);
    }
}