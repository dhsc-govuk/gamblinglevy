using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
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
// organisation/user. Mirrors the FundingSpend eligibility preamble so the feedback commands share one gate. Feedback
// has no per-activity fan-out, so there are no reportable activities to resolve.
public record StrategicFeedbackContext(
    Submission Submission,
    Collection Collection,
    OrganisationId OrganisationId,
    UserId ActorId);

public class StrategicFeedbackContextResolver(
    ISubmissionRepository submissionRepository,
    ISubmissionQueries submissionQueries,
    ISubmissionFeedbackAnswerRepository feedbackRepository,
    ICollectionQueries collectionQueries,
    IOrganisationQueries organisationQueries,
    IFormModuleQueries formModuleQueries,
    PermissionsService permissionsService)
{
    public async Task<Either<SubmissionException, StrategicFeedbackContext>> ResolveAsync(Guid collectionId, CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<Either<SubmissionException, StrategicFeedbackContext>>(
                permission => ResolveWithPermission(collectionId, permission, cancellationToken),
                error => new SubmissionForbiddenException(SubmissionId.Empty(), error.Message));
    }

    // The active feedback questions in display order. Empty when the module isn't seeded/published yet — in which case
    // there are no required answers to enforce and the section completes trivially.
    public async Task<IReadOnlyList<DcFormQuestion>> GetQuestionsAsync(CancellationToken cancellationToken)
    {
        var moduleLookup = await formModuleQueries.GetByCodeAsync(SubmissionModules.StrategicFeedback, cancellationToken);
        if (moduleLookup.IsNone)
        {
            return [];
        }

        var withChildren = await formModuleQueries.GetByIdWithChildrenAsync(moduleLookup.ValueUnsafe().Id, cancellationToken);
        return withChildren.IsNone
            ? []
            : withChildren.ValueUnsafe().Questions.Where(q => q.IsActive).OrderBy(q => q.DisplayOrder).ToList();
    }

    // Rolls the "Provide strategic feedback" section to the given status and persists it. No-op when the collection was
    // never assigned the feedback module (no section row to roll up) — mirrors FundingSpend.SetSectionStatusAsync.
    public async Task SetSectionStatusAsync(StrategicFeedbackContext context, SubmissionSectionStatus status, CancellationToken cancellationToken)
    {
        if (context.Submission.Sections.All(s => s.ModuleCode != SubmissionModules.StrategicFeedback))
        {
            return;
        }

        context.Submission.SetSectionStatus(SubmissionModules.StrategicFeedback, status, context.ActorId);
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

    private async Task<Either<SubmissionException, StrategicFeedbackContext>> ResolveWithPermission(
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

        return new StrategicFeedbackContext(submission, collection, organisationId, actorId);
    }
}