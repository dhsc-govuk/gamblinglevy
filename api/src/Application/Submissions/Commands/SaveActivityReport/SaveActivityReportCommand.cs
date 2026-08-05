using Application.Activities.Commands;
using Application.Common.Interfaces.Repositories;
using Application.Submissions.Exceptions;
using Application.Submissions.Protocol;
using Domain.Submissions;
using LanguageExt;
using MediatR;

namespace Application.Submissions.Commands.SaveActivityReport;

// Saves one activity's "Enter activity key results" answers for one page. The frontend sends that page's question
// codes (including any cleared to empty); only those codes are replaced, so saving one page never wipes the other
// page's answers. SaveAsDraft skips required enforcement ("Save and come back later"); submitted values are still
// type/range-checked. A save never marks the report Completed — that is CompleteActivityReportCommand (the Check
// page's Continue), which also re-validates everything against the saved answers. The section's task-list status is
// recomputed after each save (a save on a Completed report drops it back to In progress).
public record SaveActivityReportCommand : IRequest<Either<SubmissionException, SubmissionActivityReport>>
{
    public Guid CollectionId { get; init; }
    public Guid ActivityId { get; init; }
    public IReadOnlyList<ActivityAnswerInput> Answers { get; init; } = [];
    public bool SaveAsDraft { get; init; }
}

public class SaveActivityReportCommandHandler(
    ActivityReportContextResolver resolver,
    ISubmissionActivityAnswerRepository answerRepository)
    : IRequestHandler<SaveActivityReportCommand, Either<SubmissionException, SubmissionActivityReport>>
{
    public async Task<Either<SubmissionException, SubmissionActivityReport>> Handle(
        SaveActivityReportCommand request,
        CancellationToken cancellationToken)
    {
        var context = await resolver.ResolveAsync(request.CollectionId, request.ActivityId, cancellationToken);
        return await context.MatchAsync<Either<SubmissionException, SubmissionActivityReport>>(
            ctx => SaveAsync(request, ctx, cancellationToken),
            error => error);
    }

    private async Task<Either<SubmissionException, SubmissionActivityReport>> SaveAsync(
        SaveActivityReportCommand request,
        ActivityReportContext context,
        CancellationToken cancellationToken)
    {
        var resolved = await resolver.GetResolvedQuestionsAsync(context.Activity, cancellationToken);
        var answersMap = BuildAnswersMap(request.Answers);

        var answerErrors = SubmissionAnswerValidator.Validate(resolved, answersMap, enforceRequired: !request.SaveAsDraft);
        if (answerErrors.Count > 0)
        {
            return new SubmissionAnswersInvalidException(context.Submission.Id, context.Activity.Id, answerErrors);
        }

        try
        {
            // Replace only the answers for the codes submitted on this page; other pages' answers are untouched. A
            // submitted code whose value is blank (or that no longer resolves to a visible question) is cleared.
            var submittedCodes = answersMap.Keys.ToList();
            var persistable = SubmissionAnswerValidator.CollectPersistable(resolved, answersMap);
            var answers = persistable
                .Select(p => SubmissionActivityAnswer.Record(
                    SubmissionActivityAnswerId.New(), context.Submission.Id, context.Activity.Id, p.Question.Code, p.Value, context.ActorId))
                .ToList();

            await answerRepository.ReplaceForQuestionsAsync(context.Submission.Id, context.Activity.Id, submittedCodes, answers, cancellationToken);

            var report = await resolver.UpsertReportAsync(context, completed: false, cancellationToken);
            await resolver.RecomputeSectionStatusAsync(context, cancellationToken);

            return report;
        }
        catch (Exception exception)
        {
            return new SubmissionUnknownException(context.Submission.Id, exception);
        }
    }

    // Last value wins for a repeated code; blank codes are ignored. Ordinal to match the stable question codes.
    private static Dictionary<string, string?> BuildAnswersMap(IReadOnlyList<ActivityAnswerInput> answers)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var answer in answers)
        {
            if (!string.IsNullOrWhiteSpace(answer.QuestionCode))
            {
                map[answer.QuestionCode.Trim()] = answer.Value;
            }
        }

        return map;
    }
}