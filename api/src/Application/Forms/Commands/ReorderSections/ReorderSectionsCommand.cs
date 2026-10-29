using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Forms.Audit;
using Application.Forms.Exceptions;
using Domain.AuditLogs;
using LanguageExt;
using MediatR;
using Unit = LanguageExt.Unit;

namespace Application.Forms.Commands.ReorderSections;

// Reorders sections from the supplied ordered id list (display order = list index).
public record ReorderSectionsCommand : IRequest<Either<FormDesignerException, Unit>>
{
    public IReadOnlyList<Guid> OrderedIds { get; init; } = [];
}

public class ReorderSectionsCommandHandler(
    IFormSectionRepository sectionRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService)
    : IRequestHandler<ReorderSectionsCommand, Either<FormDesignerException, Unit>>
{
    public async Task<Either<FormDesignerException, Unit>> Handle(
        ReorderSectionsCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormDesignerException, Unit>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormDesignerForbiddenException(error.Message));
    }

    private async Task<Either<FormDesignerException, Unit>> HandleWithActor(
        ReorderSectionsCommand request,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<FormDesignerException, Unit>(
            async token =>
            {
                try
                {
                    var before = await SerializeOrderAsync(token);
                    await sectionRepository.ReorderAsync(request.OrderedIds, token);
                    var after = await SerializeOrderAsync(token);

                    // Designer sections are top-level with no parent row, so the target is Guid.Empty (as for System).
                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Form,
                            Guid.Empty,
                            AuditAction.FormSectionsReordered,
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

    private async Task<string> SerializeOrderAsync(CancellationToken cancellationToken)
    {
        var sections = await sectionRepository.GetAllAsync(cancellationToken);
        return FormSnapshot.SerializeOrder(sections.Select(s => s.Id.Value));
    }
}