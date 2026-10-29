using System.Text.Json;
using Application.Collections;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Submissions.Exceptions;
using Domain.AuditLogs;
using Domain.Submissions;
using LanguageExt;
using MediatR;

namespace Application.Submissions.Commands.SaveFundingForecast;

// Saves the "Forecast spend" screen: the expected levy spend per forecast-reportable activity (Planned included —
// BLUM-7672) for each remaining quarter of the financial year (the quarters after the reporting quarter; none in
// Q4), plus a non-activity "Other planned spend" per quarter (ActivityId = null). The full set is sent on every save
// and the Forecast rows are replaced wholesale. SaveAsDraft skips required-amount enforcement; present amounts are
// still validated (>= 0). Quarters outside the remaining set are ignored.
public record QuarterSpendInput(int Quarter, decimal? Amount);

public record ActivityForecastInput(Guid? ActivityId, IReadOnlyList<QuarterSpendInput> Quarters);

public record SaveFundingForecastCommand : IRequest<Either<SubmissionException, SubmissionSectionStatus>>
{
    public Guid CollectionId { get; init; }
    public IReadOnlyList<ActivityForecastInput> Activities { get; init; } = [];
    public IReadOnlyList<QuarterSpendInput> Other { get; init; } = [];
    public bool SaveAsDraft { get; init; }
}

public class SaveFundingForecastCommandHandler(
    FundingSpendContextResolver resolver,
    ISubmissionSpendEntryRepository spendRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<SaveFundingForecastCommand, Either<SubmissionException, SubmissionSectionStatus>>
{
    public async Task<Either<SubmissionException, SubmissionSectionStatus>> Handle(
        SaveFundingForecastCommand request,
        CancellationToken cancellationToken)
    {
        var context = await resolver.ResolveAsync(request.CollectionId, cancellationToken);
        return await context.MatchAsync<Either<SubmissionException, SubmissionSectionStatus>>(
            ctx => SaveAsync(request, ctx, cancellationToken),
            error => error);
    }

    private async Task<Either<SubmissionException, SubmissionSectionStatus>> SaveAsync(
        SaveFundingForecastCommand request,
        FundingSpendContext context,
        CancellationToken cancellationToken)
    {
        var remaining = ReportingPeriod.RemainingQuarters(context.Collection.Quarter!.Value);
        var remainingSet = remaining.ToHashSet();
        var reportableSet = context.ForecastSpendActivities.Select(a => a.Id.Value).ToHashSet();

        var activityAmounts = new Dictionary<(Guid ActivityId, int Quarter), decimal?>();
        foreach (var input in request.Activities)
        {
            if (input.ActivityId is not Guid id || !reportableSet.Contains(id))
            {
                continue;
            }

            foreach (var quarter in input.Quarters.Where(q => remainingSet.Contains(q.Quarter)))
            {
                activityAmounts[(id, quarter.Quarter)] = quarter.Amount;
            }
        }

        var otherAmounts = new Dictionary<int, decimal?>();
        foreach (var quarter in request.Other.Where(q => remainingSet.Contains(q.Quarter)))
        {
            otherAmounts[quarter.Quarter] = quarter.Amount;
        }

        var errors = FundingSpendValidator.ValidateForecast(
            context.ForecastSpendSubjects, remaining, activityAmounts, otherAmounts, enforceRequired: !request.SaveAsDraft);
        if (errors.Count > 0)
        {
            return new FundingSpendInvalidException(context.Submission.Id, errors);
        }

        // The write, the section status and the audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<SubmissionException, SubmissionSectionStatus>(
            async token =>
            {
                try
                {
                    var entries = FundingSpendEntries.Forecast(context, remaining, activityAmounts, otherAmounts, dateTimeProvider.GetUtcNow());
                    await spendRepository.ReplaceForSubmissionAsync(context.Submission.Id, SpendEntryKind.Forecast, entries, token);

                    var status = await resolver.RecomputeDraftStatusAsync(context, token);

                    // Counts only — never the amounts entered.
                    var after = JsonSerializer.Serialize(new
                    {
                        module = SubmissionModules.FundingSpent,
                        lineCount = entries.Count,
                        quarters = remaining,
                        saveAsDraft = request.SaveAsDraft,
                    });
                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            context.ActorId,
                            AuditTargetType.Submission,
                            context.Submission.Id.Value,
                            AuditAction.SubmissionFundingForecastSaved,
                            before: null,
                            after,
                            organisationId: context.OrganisationId),
                        token);

                    return status;
                }
                catch (Exception exception)
                {
                    return new SubmissionUnknownException(context.Submission.Id, exception);
                }
            },
            cancellationToken);
    }
}