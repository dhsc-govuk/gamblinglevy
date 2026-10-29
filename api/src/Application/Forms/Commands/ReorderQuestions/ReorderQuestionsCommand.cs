using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Forms.Audit;
using Application.Forms.Exceptions;
using Domain.AuditLogs;
using Domain.Forms;
using LanguageExt;
using MediatR;
using Unit = LanguageExt.Unit;

namespace Application.Forms.Commands.ReorderQuestions;

// Reorders the questions within a section from the supplied ordered id list (display order = list index).
public record ReorderQuestionsCommand : IRequest<Either<FormDesignerException, Unit>>
{
    public Guid SectionId { get; init; }
    public IReadOnlyList<Guid> OrderedIds { get; init; } = [];
}

public class ReorderQuestionsCommandHandler(
    IFormQuestionRepository questionRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService)
    : IRequestHandler<ReorderQuestionsCommand, Either<FormDesignerException, Unit>>
{
    public async Task<Either<FormDesignerException, Unit>> Handle(
        ReorderQuestionsCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormDesignerException, Unit>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormDesignerForbiddenException(error.Message));
    }

    private async Task<Either<FormDesignerException, Unit>> HandleWithActor(
        ReorderQuestionsCommand request,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        var sectionId = new FormSectionId(request.SectionId);

        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<FormDesignerException, Unit>(
            async token =>
            {
                try
                {
                    var before = await SerializeSectionOrderAsync(sectionId, token);
                    await questionRepository.ReorderAsync(sectionId, request.OrderedIds, token);

                    // Re-read rather than echo the request: ids not in the list (or not in this section) keep their order.
                    var after = await SerializeSectionOrderAsync(sectionId, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Form,
                            sectionId.Value,
                            AuditAction.FormQuestionsReordered,
                            before,
                            after),
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

    private async Task<string> SerializeSectionOrderAsync(FormSectionId sectionId, CancellationToken cancellationToken)
    {
        var questions = await questionRepository.GetAllAsync(cancellationToken);
        var orderedIds = questions
            .Where(q => q.SectionId == sectionId)
            .OrderBy(q => q.DisplayOrder)
            .Select(q => q.Id.Value);
        return FormSnapshot.SerializeOrder(orderedIds);
    }
}