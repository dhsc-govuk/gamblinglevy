using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Forms.Audit;
using Application.Forms.Exceptions;
using Domain.AuditLogs;
using Domain.Categories;
using Domain.Forms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Forms.Commands.UpdateSection;

public record UpdateSectionCommand : IRequest<Either<FormDesignerException, FormSection>>
{
    public Guid Id { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public Guid? CategoryId { get; init; }
}

public class UpdateSectionCommandHandler(
    IFormSectionRepository sectionRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<UpdateSectionCommand, Either<FormDesignerException, FormSection>>
{
    public async Task<Either<FormDesignerException, FormSection>> Handle(
        UpdateSectionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormDesignerException, FormSection>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormDesignerForbiddenException(error.Message));
    }

    private async Task<Either<FormDesignerException, FormSection>> HandleWithActor(
        UpdateSectionCommand request,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        var sectionId = new FormSectionId(request.Id);
        var lookup = await sectionRepository.GetByIdAsync(sectionId, cancellationToken);
        if (lookup.IsNone)
        {
            return new FormSectionNotFoundException(sectionId);
        }

        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<FormDesignerException, FormSection>(
            async token =>
            {
                try
                {
                    var section = lookup.ValueUnsafe();
                    var before = FormSnapshot.Serialize(section);

                    section.UpdateDetails(request.Title!, request.Description, dateTimeProvider.GetUtcNow());
                    section.SetCategory(request.CategoryId.HasValue ? new CategoryId(request.CategoryId.Value) : null, dateTimeProvider.GetUtcNow());
                    var saved = await sectionRepository.UpdateAsync(section, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Form,
                            saved.Id.Value,
                            AuditAction.FormSectionUpdated,
                            before,
                            FormSnapshot.Serialize(saved)),
                        token);

                    return saved;
                }
                catch (Exception exception)
                {
                    return new FormDesignerUnknownException(exception);
                }
            },
            cancellationToken);
    }
}