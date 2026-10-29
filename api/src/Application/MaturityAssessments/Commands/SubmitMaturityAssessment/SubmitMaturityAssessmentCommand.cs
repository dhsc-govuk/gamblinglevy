using System.Text.Json;
using Application.Collections;
using Application.Common.Features;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.MaturityAssessments.Exceptions;
using Application.MaturityAssessments.Protocol;
using Domain.AuditLogs;
using Domain.MaturityAssessments;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Application.MaturityAssessments.Commands.SubmitMaturityAssessment;

// "Submit your assessment" — the final, irreversible step (BLUM-7858 AC4). Gates: every section Completed, and a
// defensive re-validation of the saved answers. Mints the annual reference number, freezes the snapshot the
// controller rendered, audits in the same transaction, and sends the confirmation email best-effort after commit. Modelled on SubmitReportCommand.
public record SubmitMaturityAssessmentCommand : IRequest<Either<MaturityAssessmentException, MaturityAssessment>>
{
    public Guid CollectionId { get; init; }

    // The rendered assessment captured by the controller just before submit, stored verbatim so the read-only
    // views and exports never drift when the catalogue's copy changes later. Opaque here (no Application -> Api
    // dependency).
    public string? SnapshotJson { get; init; }
}

public class SubmitMaturityAssessmentCommandHandler(
    MaturityAssessmentContextResolver resolver,
    IMaturityAssessmentRepository assessmentRepository,
    IMaturityAssessmentQueries assessmentQueries,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    IUserQueries userQueries,
    IMaturityAssessmentConfirmationEmailService confirmationEmailService,
    IDateTimeProvider dateTimeProvider,
    IFeatureFlags featureFlags,
    ILogger<SubmitMaturityAssessmentCommandHandler> logger)
    : IRequestHandler<SubmitMaturityAssessmentCommand, Either<MaturityAssessmentException, MaturityAssessment>>
{
    private const int MaxAttempts = 5;

    public async Task<Either<MaturityAssessmentException, MaturityAssessment>> Handle(
        SubmitMaturityAssessmentCommand request,
        CancellationToken cancellationToken)
    {
        var context = await resolver.ResolveForEditAsync(request.CollectionId, cancellationToken);
        return await context.MatchAsync<Either<MaturityAssessmentException, MaturityAssessment>>(
            ctx => SubmitAsync(request, ctx, cancellationToken),
            error => error);
    }

    private async Task<Either<MaturityAssessmentException, MaturityAssessment>> SubmitAsync(
        SubmitMaturityAssessmentCommand request,
        MaturityAssessmentContext context,
        CancellationToken cancellationToken)
    {
        var assessment = context.Assessment;

        // The collection's current forms are the assessment (BLUM-8093): a section the admin left off is neither
        // required to be complete nor validated, so its required questions cannot block the submit.
        var sectionCodes = MaturityCollectionForms.SectionCodesFor(context.Collection);
        if (!assessment.AreSectionsCompleted(sectionCodes))
        {
            return new MaturityAssessmentIncompleteException(assessment.Id);
        }

        // Every section was completed against the saved answers, but a section can be re-opened by a later draft
        // save only through RecomputeDraftStatusAsync, which drops it to In progress — so this is a backstop, not
        // the gate. It stays because it is cheap and the alternative is a submitted assessment with a hole in it.
        var included = sectionCodes.ToHashSet(StringComparer.Ordinal);
        var questions = MaturityQuestionResolver.ResolveAll().Where(q => included.Contains(q.Section)).ToList();
        var saved = await resolver.GetSavedAnswersAsync(assessment.Id, cancellationToken);
        var errors = MaturityAnswerValidator.Validate(questions, saved, enforceRequired: true, transitive: featureFlags.IsEnabled(Feature.BLUM_9087));
        if (errors.Count > 0)
        {
            return new MaturityAnswersInvalidException(assessment.Id, errors);
        }

        // Every published collection carries a financial year; a draft cannot reach this point.
        var financialYear = context.Collection.FinancialYear!;
        try
        {
            var sequence = await assessmentQueries.CountSubmittedForFinancialYearAsync(financialYear, cancellationToken) + 1;
            assessment.Submit(MaturityAssessmentReference.Format(financialYear, sequence), context.ActorId, dateTimeProvider.GetUtcNow());
            assessment.AttachSnapshot(request.SnapshotJson);
        }
        catch (Exception exception)
        {
            return new MaturityAssessmentUnknownException(assessment.Id, exception);
        }

        for (var attempt = 1; ; attempt++)
        {
            // One transaction per attempt: a unique violation aborts the PostgreSQL transaction it happens in, so the
            // retry cannot share it. The submit and its audit row commit together (BLUM-8748).
            var result = await transactionRunner.RunAsync<MaturityAssessmentException, MaturityAssessment>(
                async token =>
                {
                    try
                    {
                        await assessmentRepository.UpdateDetectingReferenceConflictAsync(assessment, token);
                        await AppendAuditAsync(context, token);

                        return assessment;
                    }
                    catch (MaturityAssessmentReferenceConflictException conflict)
                    {
                        return conflict;
                    }
                    catch (Exception exception)
                    {
                        return new MaturityAssessmentUnknownException(assessment.Id, exception);
                    }
                },
                cancellationToken);

            // Every retry collided — the conflict surfaces as a retryable 409 rather than an opaque 500.
            var conflicted = result.Match(_ => false, left => left is MaturityAssessmentReferenceConflictException);
            if (!conflicted || attempt >= MaxAttempts)
            {
                if (result.IsRight)
                {
                    // Best-effort, and only once the submit has committed.
                    await SendConfirmationEmailAsync(context, cancellationToken);
                }

                return result;
            }

            try
            {
                var sequence = await assessmentQueries.CountSubmittedForFinancialYearAsync(financialYear, cancellationToken) + 1;
                assessment.AssignReference(MaturityAssessmentReference.Format(financialYear, sequence), context.ActorId, dateTimeProvider.GetUtcNow());
            }
            catch (Exception exception)
            {
                return new MaturityAssessmentUnknownException(assessment.Id, exception);
            }
        }
    }

    private async Task AppendAuditAsync(MaturityAssessmentContext context, CancellationToken cancellationToken)
    {
        await auditLogRepository.AppendAsync(
            AuditLog.Create(
                context.ActorId,
                AuditTargetType.MaturityAssessment,
                context.Assessment.Id.Value,
                AuditAction.MaturityAssessmentSubmitted,
                before: null,
                after: JsonSerializer.Serialize(new
                {
                    collectionId = context.Collection.Id.Value,
                    financialYear = context.Collection.FinancialYear,
                    referenceNumber = context.Assessment.ReferenceNumber,
                    submittedAt = context.Assessment.SubmittedAt,
                }),
                context.Organisation.Id),
            cancellationToken);
    }

    private async Task SendConfirmationEmailAsync(MaturityAssessmentContext context, CancellationToken cancellationToken)
    {
        try
        {
            var userLookup = await userQueries.FindById(context.ActorId, cancellationToken);
            if (userLookup.IsNone)
            {
                logger.LogWarning(
                    "Maturity assessment {Reference} submitted but submitting user {UserId} was not found; confirmation email skipped",
                    context.Assessment.ReferenceNumber,
                    context.ActorId.Value);
                return;
            }

            var confirmation = new MaturityAssessmentConfirmation(
                context.Collection.Id.Value,
                context.Assessment.ReferenceNumber!,
                context.Assessment.SubmittedAt!.Value,
                CollectionPeriodLabel.For(context.Collection),
                context.Collection.Name,
                context.Organisation.Name);

            var result = await confirmationEmailService.SendConfirmationAsync(
                userLookup.ValueUnsafe().Email,
                confirmation,
                cancellationToken);

            result.IfLeft(e => logger.LogWarning(
                e,
                "Confirmation email failed for maturity assessment {Reference}; submit not affected",
                context.Assessment.ReferenceNumber));
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Confirmation email failed for maturity assessment {Reference}; submit not affected",
                context.Assessment.ReferenceNumber);
        }
    }
}