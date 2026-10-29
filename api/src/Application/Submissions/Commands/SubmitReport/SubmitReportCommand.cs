using System.Globalization;
using Application.Collections;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.MaturityAssessments.Prefill;
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
    ActivityResultCarryForward carryForward,
    MaturityPrefillRefresher maturityPrefillRefresher,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider,
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

        var notReady = RefuseUnlessReady(submission);
        if (notReady is not null)
        {
            return notReady;
        }

        // Reference sequence is scoped to the financial year + quarter (the reference prefix), counted across every
        // collection sharing that period, so two collections in the same quarter never both mint sequence 1. The
        // global unique index on ReferenceNumber is the final backstop: a concurrent submit that grabbed the same
        // sequence first throws SubmissionReferenceConflictException, and we recompute + retry rather than 500.
        const int maxAttempts = 5;

        var financialYear = collection.FinancialYear!;
        var quarter = collection.Quarter!.Value;

        for (var attempt = 1; ; attempt++)
        {
            string before;
            try
            {
                var sequence = await submissionQueries.CountSubmittedForPeriodAsync(financialYear, quarter, cancellationToken) + 1;
                before = SubmissionSnapshot.Serialize(submission);
                submission.Submit(SubmissionReference.Format(financialYear, quarter, sequence), actorId, utcNow);
                submission.AttachReportSnapshot(request.ReportSnapshotJson);
            }
            catch (Exception exception)
            {
                return new SubmissionUnknownException(submission.Id, exception);
            }

            // One transaction per attempt: a unique violation aborts the PostgreSQL transaction it happens in, so the
            // retry cannot share it. The submit and its audit row commit together (BLUM-8748) — a failed audit now
            // rolls the submit back too, so the LA's retry is not refused as "already submitted".
            var result = await transactionRunner.RunAsync<SubmissionException, Submission>(
                async token =>
                {
                    try
                    {
                        await submissionRepository.UpdateDetectingReferenceConflictAsync(submission, token);

                        await auditLogRepository.AppendAsync(
                            AuditLog.Create(
                                actorId,
                                AuditTargetType.Submission,
                                submission.Id.Value,
                                AuditAction.SubmissionSubmitted,
                                before,
                                after: SubmissionSnapshot.Serialize(submission),
                                organisationId: organisationId),
                            token);

                        return submission;
                    }
                    catch (SubmissionReferenceConflictException conflict)
                    {
                        return conflict;
                    }
                    catch (Exception exception)
                    {
                        return new SubmissionUnknownException(submission.Id, exception);
                    }
                },
                cancellationToken);

            // Every retry collided — the conflict surfaces as a retryable 409 rather than an opaque 500.
            var conflicted = result.Match(_ => false, left => left is SubmissionReferenceConflictException);
            if (!conflicted || attempt >= maxAttempts)
            {
                if (result.IsRight)
                {
                    // This report has just become "previously submitted data", which is the input the carry-forward
                    // reads (BLUM-7671). An organisation can have a second quarterly window open and already
                    // confirmed — that report was seeded before this one existed, and nothing else would ever ask it
                    // to reconfirm. After the commit, not inside it: it changes other reports, and it logs and swallows
                    // its own failures, which inside the transaction would abort it and take the submit with it.
                    await carryForward.CopyIntoOtherOpenSubmissionsAsync(organisation, collectionId, actorId, cancellationToken);

                    // BLUM-6954: confirmation email to the submitting LA user, only once the submit has committed.
                    // Best-effort — a send failure is logged and swallowed, never undoing the submit.
                    await SendConfirmationEmailAsync(submission, collection, organisation, actorId, cancellationToken);

                    // The maturity assessment pre-fills from submitted reports too, and only did so when it was
                    // started (BLUM-8927): an assessment the LA had already opened would never see this report.
                    // After the commit and best-effort, like the carry-forward — but no token: nothing else would
                    // ever run this for the report, so a dropped connection must not cancel it.
                    await maturityPrefillRefresher.RefreshOpenAssessmentsAsync(organisation, collectionId, actorId);
                }

                return result;
            }

            // The rollback cleared the change tracker, so the instance above is detached and would be re-attached with
            // Update(), writing back every column as it was read at the start of the request — a concurrent change
            // (ActivitiesReviewRequired raised by an activity edit, or another submit) would be overwritten. So the
            // retry starts again from the current row and asks the readiness questions again.
            try
            {
                var reloaded = await submissionQueries.GetByCollectionAndOrgAsync(collectionId, organisationId, cancellationToken);
                if (reloaded.IsNone)
                {
                    return new SubmissionDoesNotExistException(collectionId);
                }

                submission = reloaded.ValueUnsafe();
            }
            catch (Exception exception)
            {
                return new SubmissionUnknownException(submission.Id, exception);
            }

            notReady = RefuseUnlessReady(submission);
            if (notReady is not null)
            {
                return notReady;
            }
        }
    }

    private static SubmissionException? RefuseUnlessReady(Submission submission)
    {
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

        return null;
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

            var recipient = userLookup.ValueUnsafe();
            var confirmation = new SubmissionConfirmation(
                collection.Id.Value,
                submission.ReferenceNumber!,
                submission.SubmittedAt!.Value,
                quarterLabel,
                collection.Name,
                organisation.Name,
                recipient.Name.ToString());

            var result = await confirmationEmailService.SendConfirmationAsync(
                recipient.Email,
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