using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Submissions.Audit;
using Application.Submissions.Exceptions;
using Domain.AuditLogs;
using Domain.Collections;
using Domain.Organisations;
using Domain.Submissions;
using Domain.Users;
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
    ActivityResultCarryForward carryForward,
    ActivityReportContextResolver activityReportResolver,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
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
        var utcNow = dateTimeProvider.GetUtcNow();

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

        if (!SubmissionEligibility.IsOpen(collection, utcNow))
        {
            return new SubmissionCollectionNotOpenException(collectionId, collection.GetStatus(utcNow));
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

        // The confirmation and its audit row commit together (BLUM-8748). The carry-forward and the recompute run after
        // the commit, not inside it: the carry-forward logs and swallows its own failures, and a failed statement
        // aborts the PostgreSQL transaction it runs in, so inside it a fault there — every time, if it is a bad row —
        // would roll the confirmation back and leave the LA unable to confirm or submit at all.
        var confirmed = await transactionRunner.RunAsync<SubmissionException, Submission>(
            async token =>
            {
                try
                {
                    var before = SubmissionSnapshot.Serialize(submission);
                    submission.ConfirmActivities(actorId, utcNow);
                    await submissionRepository.UpdateAsync(submission, token);

                    // Appended before the recompute below changes the section status, so it records the confirmation
                    // itself.
                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Submission,
                            submission.Id.Value,
                            AuditAction.SubmissionActivitiesConfirmed,
                            before,
                            after: SubmissionSnapshot.Serialize(submission),
                            organisationId: organisationId),
                        token);

                    return submission;
                }
                catch (Exception exception)
                {
                    return new SubmissionUnknownException(submission.Id, exception);
                }
            },
            cancellationToken);

        return await confirmed.MatchAsync<Either<SubmissionException, Submission>>(
            saved => CarryForwardAndRecompute(saved, collection, organisationId, actorId, cancellationToken),
            e => e);
    }

    private async Task<Either<SubmissionException, Submission>> CarryForwardAndRecompute(
        Submission submission,
        Collection collection,
        OrganisationId organisationId,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        try
        {
            // A finished activity's previously submitted results are seeded as real answer rows the LA owns and
            // can clear (BLUM-7671). Confirming is the right moment: it is the step that unlocks the sections, and
            // it is repeated whenever the activity list changes, so a newly finished activity picks its history up
            // on the next confirm. Self-contained and self-logging — it never fails the confirmation.
            await carryForward.CopyForFinishedActivitiesAsync(submission, collection, organisationId, actorId, cancellationToken);

            // Roll the key-results section up here as well as on every save. Confirming is the only moment that
            // reaches this when the organisation has NOTHING to report in that section — every other caller needs a
            // reportable activity id to get that far — so an LA whose activities are all Planned or drafts would
            // otherwise leave the section Not started and could never submit. Runs after the carry-forward so it sees
            // the carried answers. A failure here answers 500 with the confirmation already committed, and confirming
            // again re-runs both steps, so the status re-derives rather than being left stale.
            await activityReportResolver.RecomputeSectionStatusAsync(submission, organisationId, actorId, cancellationToken);

            return submission;
        }
        catch (Exception exception)
        {
            return new SubmissionUnknownException(submission.Id, exception);
        }
    }
}