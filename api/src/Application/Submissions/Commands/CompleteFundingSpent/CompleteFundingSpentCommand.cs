using Application.Collections;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Forms.Answers;
using Application.Submissions.Audit;
using Application.Submissions.Exceptions;
using Domain.AuditLogs;
using Domain.Submissions;
using LanguageExt;
using MediatR;

namespace Application.Submissions.Commands.CompleteFundingSpent;

// The "Check your spending totals" Continue: validates that every reportable activity (and the non-activity "other"
// lines) has an actual amount and a forecast for every remaining quarter, then marks the "Record funding spent"
// section Completed. The equivalent of SaveActivityReport's non-draft save for the whole section.
public record CompleteFundingSpentCommand : IRequest<Either<SubmissionException, SubmissionSectionStatus>>
{
    public Guid CollectionId { get; init; }
}

public class CompleteFundingSpentCommandHandler(
    FundingSpendContextResolver resolver,
    ISubmissionSpendEntryRepository spendRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner)
    : IRequestHandler<CompleteFundingSpentCommand, Either<SubmissionException, SubmissionSectionStatus>>
{
    public async Task<Either<SubmissionException, SubmissionSectionStatus>> Handle(
        CompleteFundingSpentCommand request,
        CancellationToken cancellationToken)
    {
        var context = await resolver.ResolveAsync(request.CollectionId, cancellationToken);
        return await context.MatchAsync<Either<SubmissionException, SubmissionSectionStatus>>(
            ctx => CompleteAsync(ctx, cancellationToken),
            error => error);
    }

    private async Task<Either<SubmissionException, SubmissionSectionStatus>> CompleteAsync(
        FundingSpendContext context,
        CancellationToken cancellationToken)
    {
        var remaining = ReportingPeriod.RemainingQuarters(context.Collection.Quarter!.Value);

        var entries = await spendRepository.GetForSubmissionAsync(context.Submission.Id, cancellationToken);

        var actualByActivity = new Dictionary<Guid, decimal?>();
        decimal? otherActual = null;
        foreach (var entry in entries.Where(e => e.Kind.IsActual))
        {
            if (entry.ActivityId is null)
            {
                otherActual = entry.Amount;
            }
            else
            {
                actualByActivity[entry.ActivityId.Value] = entry.Amount;
            }
        }

        var forecastByActivityQuarter = new Dictionary<(Guid, int), decimal?>();
        var otherForecast = new Dictionary<int, decimal?>();
        foreach (var entry in entries.Where(e => e.Kind.IsForecast))
        {
            if (entry.Quarter is not int quarter)
            {
                continue;
            }

            if (entry.ActivityId is null)
            {
                otherForecast[quarter] = entry.Amount;
            }
            else
            {
                forecastByActivityQuarter[(entry.ActivityId.Value, quarter)] = entry.Amount;
            }
        }

        // Each screen is held to its own activity set: a Planned activity owes a forecast but never an actual.
        var errors = new List<AnswerValidationError>();
        errors.AddRange(FundingSpendValidator.ValidateActuals(context.ActualSpendSubjects, actualByActivity, otherActual, enforceRequired: true));
        errors.AddRange(FundingSpendValidator.ValidateForecast(context.ForecastSpendSubjects, remaining, forecastByActivityQuarter, otherForecast, enforceRequired: true));
        if (errors.Count > 0)
        {
            return new FundingSpendInvalidException(context.Submission.Id, errors);
        }

        // The section status and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<SubmissionException, SubmissionSectionStatus>(
            async token =>
            {
                try
                {
                    var before = SubmissionSnapshot.Serialize(context.Submission);
                    await resolver.SetSectionStatusAsync(context, SubmissionSectionStatus.Completed, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            context.ActorId,
                            AuditTargetType.Submission,
                            context.Submission.Id.Value,
                            AuditAction.SubmissionFundingSpentCompleted,
                            before,
                            after: SubmissionSnapshot.Serialize(context.Submission),
                            organisationId: context.OrganisationId),
                        token);

                    return SubmissionSectionStatus.Completed;
                }
                catch (Exception exception)
                {
                    return new SubmissionUnknownException(context.Submission.Id, exception);
                }
            },
            cancellationToken);
    }
}