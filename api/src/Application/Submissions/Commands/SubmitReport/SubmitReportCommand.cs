using System.Globalization;
using Application.Collections;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Submissions.Exceptions;
using Domain.Collections;
using Domain.Organisations;
using Domain.Submissions;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.Submissions.Commands.SubmitReport;

// "Submit your quarterly report" — the final, irreversible step. Validates the report is complete (activities
// confirmed + every section Completed + nothing flagged for review), generates the reference number and records the
// submission. The report is read-only afterwards: every write command/resolver rejects a submitted report.
// Modelled on ConfirmActivitiesCommand (root mutation: load tracked submission, gate, mutate, UpdateAsync).
public record SubmitReportCommand : IRequest<Either<SubmissionException, Submission>>
{
    public Guid CollectionId { get; init; }

    // The rendered "copy for your records" captured by the controller from the live report just before submit, stored
    // verbatim on the submission so the export never drifts when activities/forms are edited afterwards. Treated as an
    // opaque blob here (no Application -> Api dependency). Null falls back to a live render at export time.
    public string? ReportSnapshotJson { get; init; }
}

public class SubmitReportCommandHandler(
    ISubmissionRepository submissionRepository,
    ISubmissionQueries submissionQueries,
    ICollectionQueries collectionQueries,
    IOrganisationQueries organisationQueries,
    IUserQueries userQueries,
    ISubmissionConfirmationEmailService confirmationEmailService,
    PermissionsService permissionsService,
    ILogger<SubmitReportCommandHandler> logger)
    : IRequestHandler<SubmitReportCommand, Either<SubmissionException, Submission>>
{
    public async Task<Either<SubmissionException, Submission>> Handle(
        SubmitReportCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetOrganisationPermissions().MatchAsync<Either<SubmissionException, Submission>>(
            permission => HandleWithPermission(request, permission, cancellationToken),
            error => new SubmissionForbiddenException(SubmissionId.Empty(), error.Message));
    }

    private async Task<Either<SubmissionException, Submission>> HandleWithPermission(
        SubmitReportCommand request,
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

        if (!submission.AreActivitiesConfirmed)
        {
            return new SubmissionActivitiesNotConfirmedException(submission.Id);
        }

        if (submission.ActivitiesReviewRequired || !submission.AreAllSectionsCompleted)
        {
            return new SubmissionNotReadyToSubmitException(submission.Id);
        }

        // Reference sequence is scoped to the financial year + quarter (the reference prefix), counted across every
        // collection sharing that period, so two collections in the same quarter never both mint sequence 1. The
        // global unique index on ReferenceNumber is the final backstop: a concurrent submit that grabbed the same
        // sequence first throws SubmissionReferenceConflictException, and we recompute + retry rather than 500.
        const int maxAttempts = 5;

        try
        {
            var financialYear = collection.FinancialYear!;
            var quarter = collection.Quarter!.Value;

            var sequence = await submissionQueries.CountSubmittedForPeriodAsync(financialYear, quarter, cancellationToken) + 1;
            submission.Submit(SubmissionReference.Format(financialYear, quarter, sequence), actorId);
            submission.AttachReportSnapshot(request.ReportSnapshotJson);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await submissionRepository.UpdateDetectingReferenceConflictAsync(submission, cancellationToken);

                    // BLUM-6954: confirmation email to the submitting LA user. Best-effort — the submission is now
                    // persisted and irreversible, so a send failure is logged and swallowed, never rolling back the submit.
                    await SendConfirmationEmailAsync(submission, collection, organisation, actorId, cancellationToken);

                    return submission;
                }
                catch (SubmissionReferenceConflictException) when (attempt < maxAttempts)
                {
                    sequence = await submissionQueries.CountSubmittedForPeriodAsync(financialYear, quarter, cancellationToken) + 1;
                    submission.AssignReference(SubmissionReference.Format(financialYear, quarter, sequence), actorId);
                }
            }
        }
        catch (SubmissionReferenceConflictException conflict)
        {
            // Every retry collided — surface a retryable 409 rather than an opaque 500.
            return conflict;
        }
        catch (Exception exception)
        {
            return new SubmissionUnknownException(submission.Id, exception);
        }
    }

    // Sends the "your quarterly report has been submitted" confirmation (BLUM-6954). Best-effort: any failure
    // (recipient not found, SendGrid error, exception) is logged and swallowed so the already-persisted submit stands.
    private async Task SendConfirmationEmailAsync(
        Submission submission,
        Collection collection,
        Organisation organisation,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        try
        {
            var userLookup = await userQueries.FindById(actorId, cancellationToken);
            if (userLookup.IsNone)
            {
                logger.LogWarning(
                    "Submission {Reference} submitted but submitting user {UserId} was not found; confirmation email skipped",
                    submission.ReferenceNumber,
                    actorId.Value);
                return;
            }

            var (start, end) = ReportingPeriod.Derive(collection.FinancialYear!, collection.Quarter);
            var quarterLabel = string.Format(
                CultureInfo.InvariantCulture,
                "Quarter {0} ({1:d MMMM yyyy} to {2:d MMMM yyyy})",
                collection.Quarter!.Value,
                start,
                end);

            var confirmation = new SubmissionConfirmation(
                collection.Id.Value,
                submission.ReferenceNumber!,
                submission.SubmittedAt!.Value,
                quarterLabel,
                collection.Name,
                organisation.Name);

            var result = await confirmationEmailService.SendConfirmationAsync(
                userLookup.ValueUnsafe().Email,
                confirmation,
                cancellationToken);

            result.IfLeft(e => logger.LogWarning(
                e,
                "Confirmation email failed for submission {Reference}; submit not affected",
                submission.ReferenceNumber));
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Confirmation email failed for submission {Reference}; submit not affected",
                submission.ReferenceNumber);
        }
    }
}