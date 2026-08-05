using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Submissions.Exceptions;
using Domain.Collections;
using Domain.Submissions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Submissions.Commands.ConfirmActivities;

// "Confirm your activities for this quarter" — marks the activities-list step Completed, unlocking the report
// sections, and clears any review flags raised by activity-list changes since the previous confirmation.
public record ConfirmActivitiesCommand : IRequest<Either<SubmissionException, Submission>>
{
    public Guid CollectionId { get; init; }
}

public class ConfirmActivitiesCommandHandler(
    ISubmissionRepository submissionRepository,
    ISubmissionQueries submissionQueries,
    ICollectionQueries collectionQueries,
    IOrganisationQueries organisationQueries,
    PermissionsService permissionsService)
    : IRequestHandler<ConfirmActivitiesCommand, Either<SubmissionException, Submission>>
{
    public async Task<Either<SubmissionException, Submission>> Handle(
        ConfirmActivitiesCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions().MatchAsync<Either<SubmissionException, Submission>>(
            permission => HandleWithPermission(request, permission, cancellationToken),
            error => new SubmissionForbiddenException(SubmissionId.Empty(), error.Message));
    }

    private async Task<Either<SubmissionException, Submission>> HandleWithPermission(
        ConfirmActivitiesCommand request,
        Permission permission,
        CancellationToken cancellationToken)
    {
        if (permission.OrganisationId.IsNone || permission.UserId.IsNone)
        {
            return new SubmissionForbiddenException(SubmissionId.Empty(), "You are not associated with an organisation.");
        }

        var organisationId = permission.OrganisationId.ValueUnsafe();
        var actorId = permission.UserId.ValueUnsafe();
        var collectionId = new CollectionId(request.CollectionId);

        var collectionLookup = await collectionQueries.GetByIdAsync(collectionId, cancellationToken: cancellationToken);
        if (collectionLookup.IsNone)
        {
            return new SubmissionCollectionNotEligibleException(collectionId);
        }

        var organisationLookup = await organisationQueries.GetOrganisationById(organisationId, cancellationToken);
        if (organisationLookup.IsNone)
        {
            return new SubmissionCollectionNotEligibleException(collectionId);
        }

        var organisation = organisationLookup.ValueUnsafe();
        var collection = collectionLookup.ValueUnsafe();
        if (!SubmissionEligibility.IsQuarterly(collection) || !SubmissionEligibility.IsAssignedTo(collection, organisation))
        {
            return new SubmissionCollectionNotEligibleException(collectionId);
        }

        if (!SubmissionEligibility.IsOpen(collection))
        {
            return new SubmissionCollectionNotOpenException(collectionId, collection.GetStatus());
        }

        var submissionLookup = await submissionQueries.GetByCollectionAndOrgAsync(collectionId, organisationId, cancellationToken);
        if (submissionLookup.IsNone)
        {
            return new SubmissionDoesNotExistException(collectionId);
        }

        var submission = submissionLookup.ValueUnsafe();
        if (submission.IsSubmitted)
        {
            return new SubmissionAlreadySubmittedException(submission.Id);
        }

        try
        {
            submission.ConfirmActivities(actorId);
            await submissionRepository.UpdateAsync(submission, cancellationToken);
            return submission;
        }
        catch (Exception exception)
        {
            return new SubmissionUnknownException(submission.Id, exception);
        }
    }
}