using Application.Common.Interfaces.Repositories;
using Application.Submissions.Exceptions;
using Application.Submissions.Protocol;
using Domain.Submissions;
using LanguageExt;
using MediatR;

namespace Application.Submissions.Commands.CompleteActivityReport;

// The "Check your answers" Continue for one activity: validates that every required visible question has a SAVED
// answer (the pages save drafts independently via the per-page merge), then marks the activity's report Completed
// and recomputes the section roll-up. The frontend posts no body — the already-saved draft answers are validated.
// The validated set is re-persisted wholesale, which prunes orphaned rows (codes that no longer resolve to a
// question, e.g. a removed custom key result) and answers hidden by a conditional parent change.
public record CompleteActivityReportCommand : IRequest<Either<SubmissionException, SubmissionActivityReport>>
{
    public Guid CollectionId { get; init; }
    public Guid ActivityId { get; init; }
}

public class CompleteActivityReportCommandHandler(
    ActivityReportContextResolver resolver,
    ISubmissionActivityAnswerRepository answerRepository)
    : IRequestHandler<CompleteActivityReportCommand, Either<SubmissionException, SubmissionActivityReport>>
{
    public async Task<Either<SubmissionException, SubmissionActivityReport>> Handle(
        CompleteActivityReportCommand request,
        CancellationToken cancellationToken)
    {
        var context = await resolver.ResolveAsync(request.CollectionId, request.ActivityId, cancellationToken);
        return await context.MatchAsync<Either<SubmissionException, SubmissionActivityReport>>(
            ctx => CompleteAsync(ctx, cancellationToken),
            error => error);
    }

    private async Task<Either<SubmissionException, SubmissionActivityReport>> CompleteAsync(
        ActivityReportContext context,
        CancellationToken cancellationToken)
    {
        var resolved = await resolver.GetResolvedQuestionsAsync(context.Activity, cancellationToken);
        var saved = await answerRepository.GetForActivityAsync(context.Submission.Id, context.Activity.Id, cancellationToken);
        var answersMap = saved.ToDictionary(a => a.QuestionCode, a => (string?)a.Value, StringComparer.Ordinal);

        var answerErrors = SubmissionAnswerValidator.Validate(resolved, answersMap, enforceRequired: true);
        if (answerErrors.Count > 0)
        {
            return new SubmissionAnswersInvalidException(context.Submission.Id, context.Activity.Id, answerErrors);
        }

        try
        {
            var persistable = SubmissionAnswerValidator.CollectPersistable(resolved, answersMap);
            var answers = persistable
                .Select(p => SubmissionActivityAnswer.Record(
                    SubmissionActivityAnswerId.New(), context.Submission.Id, context.Activity.Id, p.Question.Code, p.Value, context.ActorId))
                .ToList();

            await answerRepository.ReplaceForActivityAsync(context.Submission.Id, context.Activity.Id, answers, cancellationToken);

            var report = await resolver.UpsertReportAsync(context, completed: true, cancellationToken);
            await resolver.RecomputeSectionStatusAsync(context, cancellationToken);

            return report;
        }
        catch (Exception exception)
        {
            return new SubmissionUnknownException(context.Submission.Id, exception);
        }
    }
}