using Application.Common.Interfaces.Repositories;
using Application.Submissions.Exceptions;
using Domain.Submissions;
using LanguageExt;
using MediatR;

namespace Application.Submissions.Commands.SaveFundingActuals;

// Saves the "Activity spending" screen: the levy actually spent this quarter per reportable activity, plus the
// single non-activity "Other spend this quarter" (ActivityId = null). The full set is sent on every save and the
// Actual rows are replaced wholesale. SaveAsDraft skips required-amount enforcement ("Save and come back later");
// amounts that are present are still validated (>= 0). The section's task-list status is recomputed after the save.
public record ActivitySpendInput(Guid? ActivityId, decimal? Amount);

public record SaveFundingActualsCommand : IRequest<Either<SubmissionException, SubmissionSectionStatus>>
{
    public Guid CollectionId { get; init; }
    public IReadOnlyList<ActivitySpendInput> Activities { get; init; } = [];
    public decimal? OtherAmount { get; init; }
    public bool SaveAsDraft { get; init; }
}

public class SaveFundingActualsCommandHandler(
    FundingSpendContextResolver resolver,
    ISubmissionSpendEntryRepository spendRepository)
    : IRequestHandler<SaveFundingActualsCommand, Either<SubmissionException, SubmissionSectionStatus>>
{
    public async Task<Either<SubmissionException, SubmissionSectionStatus>> Handle(
        SaveFundingActualsCommand request,
        CancellationToken cancellationToken)
    {
        var context = await resolver.ResolveAsync(request.CollectionId, cancellationToken);
        return await context.MatchAsync<Either<SubmissionException, SubmissionSectionStatus>>(
            ctx => SaveAsync(request, ctx, cancellationToken),
            error => error);
    }

    private async Task<Either<SubmissionException, SubmissionSectionStatus>> SaveAsync(
        SaveFundingActualsCommand request,
        FundingSpendContext context,
        CancellationToken cancellationToken)
    {
        var reportableIds = context.ReportableActivities.Select(a => a.Id).ToList();
        var reportableSet = reportableIds.Select(id => id.Value).ToHashSet();

        var activityAmounts = new Dictionary<Guid, decimal?>();
        foreach (var input in request.Activities)
        {
            if (input.ActivityId is Guid id && reportableSet.Contains(id))
            {
                activityAmounts[id] = input.Amount;
            }
        }

        var errors = FundingSpendValidator.ValidateActuals(
            reportableIds, activityAmounts, request.OtherAmount, enforceRequired: !request.SaveAsDraft);
        if (errors.Count > 0)
        {
            return new FundingSpendInvalidException(context.Submission.Id, errors);
        }

        try
        {
            var entries = new List<SubmissionSpendEntry>();
            foreach (var id in reportableIds)
            {
                if (activityAmounts.TryGetValue(id.Value, out var amount) && amount is decimal value)
                {
                    entries.Add(SubmissionSpendEntry.Actual(SubmissionSpendEntryId.New(), context.Submission.Id, id, value, context.ActorId));
                }
            }

            if (request.OtherAmount is decimal other)
            {
                entries.Add(SubmissionSpendEntry.Actual(SubmissionSpendEntryId.New(), context.Submission.Id, null, other, context.ActorId));
            }

            await spendRepository.ReplaceForSubmissionAsync(context.Submission.Id, SpendEntryKind.Actual, entries, cancellationToken);

            return await resolver.RecomputeDraftStatusAsync(context, cancellationToken);
        }
        catch (Exception exception)
        {
            return new SubmissionUnknownException(context.Submission.Id, exception);
        }
    }
}