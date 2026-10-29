using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Permissions;
using Application.Submissions.Exceptions;
using Domain.Collections;
using Domain.Organisations;
using Domain.Submissions;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Application.Submissions;

// The report every quarterly-report write acts on, once the caller is allowed to change it.
public record QuarterlyReportWriteContext(
    Submission Submission,
    Collection Collection,
    Organisation Organisation,
    UserId ActorId)
{
    public OrganisationId OrganisationId => Organisation.Id;
}

// The one gate in front of every write to an LA's quarterly report, whichever section it touches: the caller belongs
// to an organisation, the collection is a quarterly one assigned to that organisation, its window is open, the
// organisation has started the report, has not submitted it, and has confirmed its activities. The three section
// resolvers each ran this same preamble inline; it lives here so the offline upload (BLUM-8310), which only learns
// the section from inside the file, can pass the gate before it reads a byte of that file.
//
// The order is part of the contract: each check answers with the exception the one before it cannot already have
// answered, and SubmissionAccessControlTests pins the resulting status codes for every route.
public class QuarterlyReportWriteGate(
    ISubmissionQueries submissionQueries,
    ICollectionQueries collectionQueries,
    IOrganisationQueries organisationQueries,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
{
    public async Task<Either<SubmissionException, QuarterlyReportWriteContext>> ResolveAsync(
        Guid collectionId,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions()
            .MatchAsync<Either<SubmissionException, QuarterlyReportWriteContext>>(
                permission => ResolveWithPermission(collectionId, permission, cancellationToken),
                error => new SubmissionForbiddenException(SubmissionId.Empty(), error.Message));
    }

    private async Task<Either<SubmissionException, QuarterlyReportWriteContext>> ResolveWithPermission(
        Guid collectionId,
        Permission permission,
        CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.GetUtcNow();

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

        if (!SubmissionEligibility.IsOpen(collection, utcNow))
        {
            return new SubmissionCollectionNotOpenException(collectionRef, collection.GetStatus(utcNow));
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

        return new QuarterlyReportWriteContext(submission, collection, organisation, actorId);
    }
}