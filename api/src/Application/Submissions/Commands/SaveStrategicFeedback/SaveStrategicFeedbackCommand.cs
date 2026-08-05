using Application.Common.Interfaces.Repositories;
using Application.Submissions.Exceptions;
using Domain.Submissions;
using LanguageExt;
using MediatR;

namespace Application.Submissions.Commands.SaveStrategicFeedback;

// Saves the "Provide strategic feedback" answers for one page. The frontend sends that page's questions (including any
// cleared to empty); only those question codes are replaced, so saving one page never wipes the other page's answers.
// SaveAsDraft skips required enforcement ("Save and come back later"); answers that are present are still length-checked.
// The section's task-list status is recomputed after the save (never reaches Completed via a draft save — that is the
// Check page's Continue).
public record FeedbackAnswerInput(string QuestionCode, string? Value);

public record SaveStrategicFeedbackCommand : IRequest<Either<SubmissionException, SubmissionSectionStatus>>
{
    public Guid CollectionId { get; init; }
    public IReadOnlyList<FeedbackAnswerInput> Answers { get; init; } = [];
    public bool SaveAsDraft { get; init; }
}

public class SaveStrategicFeedbackCommandHandler(
    StrategicFeedbackContextResolver resolver,
    ISubmissionFeedbackAnswerRepository feedbackRepository)
    : IRequestHandler<SaveStrategicFeedbackCommand, Either<SubmissionException, SubmissionSectionStatus>>
{
    public async Task<Either<SubmissionException, SubmissionSectionStatus>> Handle(
        SaveStrategicFeedbackCommand request,
        CancellationToken cancellationToken)
    {
        var context = await resolver.ResolveAsync(request.CollectionId, cancellationToken);
        return await context.MatchAsync<Either<SubmissionException, SubmissionSectionStatus>>(
            ctx => SaveAsync(request, ctx, cancellationToken),
            error => error);
    }

    private async Task<Either<SubmissionException, SubmissionSectionStatus>> SaveAsync(
        SaveStrategicFeedbackCommand request,
        StrategicFeedbackContext context,
        CancellationToken cancellationToken)
    {
        var questions = await resolver.GetQuestionsAsync(cancellationToken);
        var answersMap = BuildAnswersMap(request.Answers);

        var errors = StrategicFeedbackAnswerValidator.Validate(questions, answersMap, enforceRequired: !request.SaveAsDraft);
        if (errors.Count > 0)
        {
            return new StrategicFeedbackInvalidException(context.Submission.Id, errors);
        }

        try
        {
            // Replace only the answers for the questions submitted on this page; other pages' answers are untouched,
            // so saving one page never wipes another's (the pages save independently).
            var submittedCodes = answersMap.Keys.ToList();
            var answers = StrategicFeedbackAnswerValidator.CollectPersistable(questions, answersMap)
                .Select(p => SubmissionFeedbackAnswer.Record(
                    SubmissionFeedbackAnswerId.New(), context.Submission.Id, p.Question.Code, p.Value, context.ActorId))
                .ToList();

            await feedbackRepository.ReplaceForQuestionsAsync(context.Submission.Id, submittedCodes, answers, cancellationToken);

            return await resolver.RecomputeDraftStatusAsync(context, cancellationToken);
        }
        catch (Exception exception)
        {
            return new SubmissionUnknownException(context.Submission.Id, exception);
        }
    }

    // Last value wins for a repeated code; blank codes are ignored. Ordinal to match the stable question codes.
    private static Dictionary<string, string?> BuildAnswersMap(IReadOnlyList<FeedbackAnswerInput> inputs)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            if (!string.IsNullOrWhiteSpace(input.QuestionCode))
            {
                map[input.QuestionCode.Trim()] = input.Value;
            }
        }

        return map;
    }
}