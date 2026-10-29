using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Submissions.Audit;
using Application.Submissions.Exceptions;
using Domain.AuditLogs;
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
    ISubmissionFeedbackAnswerRepository feedbackRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner)
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
        var questions = await resolver.GetQuestionsAsync(context.Collection, cancellationToken);
        var saved = await feedbackRepository.GetForSubmissionAsync(context.Submission.Id, cancellationToken);
        var answersMap = saved.ToDictionary(a => a.QuestionCode, a => (string?)a.Value, StringComparer.Ordinal);

        var errors = StrategicFeedbackAnswerValidator.Validate(questions, answersMap, enforceRequired: true);
        if (errors.Count > 0)
        {
            return new StrategicFeedbackInvalidException(context.Submission.Id, errors);
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
                            AuditAction.SubmissionStrategicFeedbackCompleted,
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