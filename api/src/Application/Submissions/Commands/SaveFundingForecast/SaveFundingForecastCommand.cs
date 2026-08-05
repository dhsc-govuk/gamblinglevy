using Application.Collections;
using Application.Common.Interfaces.Repositories;
using Application.Submissions.Exceptions;
using Domain.Submissions;
using LanguageExt;
using MediatR;

namespace Application.Submissions.Commands.SaveFundingForecast;

// Saves the "Forecast spend" screen: the expected levy spend per reportable activity for each remaining quarter of
// the financial year (the reporting quarter through Q4), plus a non-activity "Other planned spend" per quarter
// (ActivityId = null). The full set is sent on every save and the Forecast rows are replaced wholesale. SaveAsDraft
// skips required-amount enforcement; present amounts are still validated (>= 0). Quarters outside the remaining set
// are ignored.
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
    ISubmissionSpendEntryRepository spendRepository)
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
        var reportableIds = context.ReportableActivities.Select(a => a.Id).ToList();
        var reportableSet = reportableIds.Select(id => id.Value).ToHashSet();

        var activityAmounts = new Dictionary<(Guid, int), decimal?>();
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
            reportableIds, remaining, activityAmounts, otherAmounts, enforceRequired: !request.SaveAsDraft);
        if (errors.Count > 0)
        {
            return new FundingSpendInvalidException(context.Submission.Id, errors);
        }

        try
        {
            var entries = new List<SubmissionSpendEntry>();
            foreach (var id in reportableIds)
            {
                foreach (var quarter in remaining)
                {
                    if (activityAmounts.TryGetValue((id.Value, quarter), out var amount) && amount is decimal value)
                    {
                        entries.Add(SubmissionSpendEntry.Forecast(SubmissionSpendEntryId.New(), context.Submission.Id, id, quarter, value, context.ActorId));
                    }
                }
            }

            foreach (var quarter in remaining)
            {
                if (otherAmounts.TryGetValue(quarter, out var amount) && amount is decimal value)
                {
                    entries.Add(SubmissionSpendEntry.Forecast(SubmissionSpendEntryId.New(), context.Submission.Id, null, quarter, value, context.ActorId));
                }
            }

            await spendRepository.ReplaceForSubmissionAsync(context.Submission.Id, SpendEntryKind.Forecast, entries, cancellationToken);

            return await resolver.RecomputeDraftStatusAsync(context, cancellationToken);
        }
        catch (Exception exception)
        {
            return new SubmissionUnknownException(context.Submission.Id, exception);
        }
    }
}