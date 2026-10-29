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

namespace Application.Submissions.Commands.RegenerateReportSnapshot;

// Re-freezes an already-submitted submission's ReportSnapshotJson (BLUM-7130 CSV review): the DHSC-wide export
// omits any submission whose frozen snapshot predates a header-shape change rather than risk misaligning cells
// (see ExportSubmissionsCommand.BuildFundingCsv), so a collection submitted before such a change exports with
// funding-spend.csv/funding-summary.csv silently blank for those organisations. The replacement JSON is built by
// the caller (the Api layer, via IReportSnapshotService — Application cannot depend on Api) from today's rules and
// header shape; this command only attaches and persists it. Mirrors SubmitReportCommand's snapshot-attach step.
public record RegenerateReportSnapshotCommand(Guid CollectionId, Guid OrganisationId, string? ReportSnapshotJson)
    : IRequest<Either<SubmissionException, Submission>>;

public class RegenerateReportSnapshotCommandHandler(
    ISubmissionQueries submissionQueries,
    ISubmissionRepository submissionRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService)
    : IRequestHandler<RegenerateReportSnapshotCommand, Either<SubmissionException, Submission>>
{
    public async Task<Either<SubmissionException, Submission>> Handle(
        RegenerateReportSnapshotCommand request,
        CancellationToken cancellationToken)
    {
        var collectionId = new CollectionId(request.CollectionId);
        var organisationId = new OrganisationId(request.OrganisationId);

        var submissionLookup = await submissionQueries.GetByCollectionAndOrgAsync(collectionId, organisationId, cancellationToken);
        if (submissionLookup.IsNone)
        {
            return new SubmissionDoesNotExistException(collectionId);
        }

        var submission = submissionLookup.ValueUnsafe();
        if (!submission.IsSubmitted)
        {
            return new SubmissionNotSubmittedException(submission.Id);
        }

        // The controller resolves the DHSC admin before sending this, so a missing identity here is a caller that
        // should not be regenerating anything — refused rather than written without an actor (BLUM-8747).
        return await permissionsService.GetUserId().MatchAsync<Either<SubmissionException, Submission>>(
            actorId => RegenerateAsync(request, submission, organisationId, actorId, cancellationToken),
            error => new SubmissionForbiddenException(submission.Id, error.Message));
    }

    private async Task<Either<SubmissionException, Submission>> RegenerateAsync(
        RegenerateReportSnapshotCommand request,
        Submission submission,
        OrganisationId organisationId,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        // The new snapshot and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<SubmissionException, Submission>(
            async token =>
            {
                try
                {
                    var before = SubmissionSnapshot.Serialize(submission);
                    submission.AttachReportSnapshot(request.ReportSnapshotJson);
                    await submissionRepository.UpdateAsync(submission, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Submission,
                            submission.Id.Value,
                            AuditAction.SubmissionSnapshotRegenerated,
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
    }
}