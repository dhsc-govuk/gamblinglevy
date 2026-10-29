using System.Text.Json;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Submissions.Exceptions;
using Domain.AuditLogs;
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
    ISubmissionSpendEntryRepository spendRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    IDateTimeProvider dateTimeProvider)
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
        var reportableSet = context.ActualSpendActivities.Select(a => a.Id.Value).ToHashSet();

        var activityAmounts = new Dictionary<Guid, decimal?>();
        foreach (var input in request.Activities)
        {
            if (input.ActivityId is Guid id && reportableSet.Contains(id))
            {
                activityAmounts[id] = input.Amount;
            }
        }

        var errors = FundingSpendValidator.ValidateActuals(
            context.ActualSpendSubjects, activityAmounts, request.OtherAmount, enforceRequired: !request.SaveAsDraft);
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
                    var entries = FundingSpendEntries.Actuals(context, activityAmounts, request.OtherAmount, dateTimeProvider.GetUtcNow());
                    await spendRepository.ReplaceForSubmissionAsync(context.Submission.Id, SpendEntryKind.Actual, entries, token);

                    var status = await resolver.RecomputeDraftStatusAsync(context, token);

                    // Counts only — never the amounts entered.
                    var after = JsonSerializer.Serialize(new
                    {
                        module = SubmissionModules.FundingSpent,
                        lineCount = entries.Count,
                        saveAsDraft = request.SaveAsDraft,
                    });
                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            context.ActorId,
                            AuditTargetType.Submission,
                            context.Submission.Id.Value,
                            AuditAction.SubmissionFundingActualsSaved,
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