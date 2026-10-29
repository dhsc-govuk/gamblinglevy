using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Forms.Audit;
using Application.Forms.Exceptions;
using Domain.AuditLogs;
using Domain.Forms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;
using Unit = LanguageExt.Unit;

namespace Application.Forms.Commands.DeleteQuestion;

// Hard-deletes a question and its options. Existing answers are intentionally preserved (they reference the
// question by id with a snapshotted code/value). Blocked when another question's conditional rule depends on it.
public record DeleteQuestionCommand : IRequest<Either<FormDesignerException, Unit>>
{
    public Guid Id { get; init; }
}

public class DeleteQuestionCommandHandler(
    IFormQuestionRepository questionRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService)
    : IRequestHandler<DeleteQuestionCommand, Either<FormDesignerException, Unit>>
{
    public async Task<Either<FormDesignerException, Unit>> Handle(
        DeleteQuestionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormDesignerException, Unit>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormDesignerForbiddenException(error.Message));
    }

    private async Task<Either<FormDesignerException, Unit>> HandleWithActor(
        DeleteQuestionCommand request,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        var questionId = new FormQuestionId(request.Id);
        var lookup = await questionRepository.GetByIdAsync(questionId, cancellationToken);
        if (lookup.IsNone)
        {
            return new FormQuestionNotFoundException(questionId);
        }

        var question = lookup.ValueUnsafe();
        var referenced = await questionRepository.AnyReferencingConditionalAsync(question.Code, questionId, cancellationToken);
        if (referenced)
        {
            return new FormQuestionReferencedException(questionId, question.Code);
        }

        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<FormDesignerException, Unit>(
            async token =>
            {
                try
                {
                    var before = FormSnapshot.Serialize(question);
                    await questionRepository.DeleteAsync(questionId, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Form,
                            questionId.Value,
                            AuditAction.FormQuestionDeleted,
                            before,
                            after: null),
                        token);

                    return Unit.Default;
                }
                catch (Exception exception)
                {
                    return new FormDesignerUnknownException(exception);
                }
            },
            cancellationToken);
    }
}