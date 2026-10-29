using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Forms.Audit;
using Application.Forms.Exceptions;
using Domain.AuditLogs;
using Domain.Categories;
using Domain.Forms;
using LanguageExt;
using MediatR;

namespace Application.Forms.Commands.CreateSection;

public record CreateSectionCommand : IRequest<Either<FormDesignerException, FormSection>>
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public Guid? CategoryId { get; init; }
}

public class CreateSectionCommandHandler(
    IFormSectionRepository sectionRepository,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<CreateSectionCommand, Either<FormDesignerException, FormSection>>
{
    public async Task<Either<FormDesignerException, FormSection>> Handle(
        CreateSectionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormDesignerException, FormSection>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormDesignerForbiddenException(error.Message));
    }

    private async Task<Either<FormDesignerException, FormSection>> HandleWithActor(
        CreateSectionCommand request,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        // The change and its audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<FormDesignerException, FormSection>(
            async token =>
            {
                try
                {
                    var nextOrder = await sectionRepository.GetMaxDisplayOrderAsync(token) + 1;
                    var categoryId = request.CategoryId.HasValue ? new CategoryId(request.CategoryId.Value) : null;
                    var section = FormSection.Create(FormSectionId.New(), request.Title!, nextOrder, actorId, dateTimeProvider.GetUtcNow(), request.Description, categoryId);
                    var saved = await sectionRepository.AddAsync(section, token);

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Form,
                            saved.Id.Value,
                            AuditAction.FormSectionCreated,
                            before: null,
                            after: FormSnapshot.Serialize(saved)),
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