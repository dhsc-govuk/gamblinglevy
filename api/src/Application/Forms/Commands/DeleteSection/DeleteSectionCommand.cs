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

namespace Application.Forms.Commands.DeleteSection;

// Hard-deletes a section, but only when it contains no questions. A populated section is retired by
// deactivating it instead (BLUM-6444).
public record DeleteSectionCommand : IRequest<Either<FormDesignerException, Unit>>
{
    public Guid Id { get; init; }
}

public class DeleteSectionCommandHandler(
    IFormSectionRepository sectionRepository,
    IFormQuestionRepository questionRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService)
    : IRequestHandler<DeleteSectionCommand, Either<FormDesignerException, Unit>>
{
    public async Task<Either<FormDesignerException, Unit>> Handle(
        DeleteSectionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormDesignerException, Unit>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormDesignerForbiddenException(error.Message));
    }

    private async Task<Either<FormDesignerException, Unit>> HandleWithActor(
        DeleteSectionCommand request,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        var sectionId = new FormSectionId(request.Id);
        var lookup = await sectionRepository.GetByIdAsync(sectionId, cancellationToken);
        if (lookup.IsNone)
        {
            return new FormSectionNotFoundException(sectionId);
        }

        var questionCount = await questionRepository.CountBySectionAsync(sectionId, cancellationToken);
        if (questionCount > 0)
        {
            return new FormSectionNotEmptyException(sectionId);
        }

        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<FormDesignerException, Unit>(
            async token =>
            {
                try
                {
                    var before = FormSnapshot.Serialize(lookup.ValueUnsafe());
                    await sectionRepository.DeleteAsync(sectionId, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Form,
                            sectionId.Value,
                            AuditAction.FormSectionDeleted,
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