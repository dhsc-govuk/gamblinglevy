using Application.Common.Interfaces.Repositories;
using Application.Submissions.Exceptions;
using Domain.Submissions;
using LanguageExt;
using MediatR;

namespace Application.Submissions.Commands.CompleteStrategicFeedback;

// The "Check your answers" Continue: validates that every required feedback question has a saved answer, then marks the
// "Provide strategic feedback" section Completed. The equivalent of SaveStrategicFeedback's non-draft save for the
// whole section. The frontend posts no body — the already-saved draft answers are validated.
public record CompleteStrategicFeedbackCommand : IRequest<Either<SubmissionException, SubmissionSectionStatus>>
{
    public Guid CollectionId { get; init; }
}

public class CompleteStrategicFeedbackCommandHandler(
    StrategicFeedbackContextResolver resolver,
    ISubmissionFeedbackAnswerRepository feedbackRepository)
    : IRequestHandler<CompleteStrategicFeedbackCommand, Either<SubmissionException, SubmissionSectionStatus>>
{
    public async Task<Either<SubmissionException, SubmissionSectionStatus>> Handle(
        CompleteStrategicFeedbackCommand request,
        CancellationToken cancellationToken)
    {
        var context = await resolver.ResolveAsync(request.CollectionId, cancellationToken);
        return await context.MatchAsync<Either<SubmissionException, SubmissionSectionStatus>>(
            ctx => CompleteAsync(ctx, cancellationToken),
            error => error);
    }

    private async Task<Either<SubmissionException, SubmissionSectionStatus>> CompleteAsync(
        StrategicFeedbackContext context,
        CancellationToken cancellationToken)
    {
        var questions = await resolver.GetQuestionsAsync(cancellationToken);
        var saved = await feedbackRepository.GetForSubmissionAsync(context.Submission.Id, cancellationToken);
        var answersMap = saved.ToDictionary(a => a.QuestionCode, a => (string?)a.Value, StringComparer.Ordinal);

        var errors = StrategicFeedbackAnswerValidator.Validate(questions, answersMap, enforceRequired: true);
        if (errors.Count > 0)
        {
            return new StrategicFeedbackInvalidException(context.Submission.Id, errors);
        }

        try
        {
            await resolver.SetSectionStatusAsync(context, SubmissionSectionStatus.Completed, cancellationToken);
            return SubmissionSectionStatus.Completed;
        }
        catch (Exception exception)
        {
            return new SubmissionUnknownException(context.Submission.Id, exception);
        }
    }
}