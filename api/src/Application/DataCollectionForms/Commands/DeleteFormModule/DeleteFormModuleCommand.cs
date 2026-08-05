using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.DataCollectionForms.Exceptions;
using Domain.DataCollectionForms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.DataCollectionForms.Commands.DeleteFormModule;

public record DeleteFormModuleCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public Guid Id { get; init; }
}

public class DeleteFormModuleCommandHandler(
    IFormModuleRepository moduleRepository,
    IFormModuleQueries moduleQueries,
    PermissionsService permissionsService)
    : IRequestHandler<DeleteFormModuleCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        DeleteFormModuleCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            _ => HandleWithActor(request, cancellationToken),
            error => new FormModuleForbiddenException(FormModuleId.From(request.Id), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        DeleteFormModuleCommand request,
        CancellationToken cancellationToken)
    {
        var moduleId = FormModuleId.From(request.Id);

        var lookup = await moduleQueries.GetByIdWithChildrenAsync(moduleId, cancellationToken);
        if (lookup.IsNone)
        {
            return new FormModuleNotFoundException(moduleId);
        }

        var module = lookup.ValueUnsafe();

        try
        {
            // Soft retire rather than destructively delete — published modules may already be assigned to
            // collections, so we archive to preserve referential history.
            module.Archive();
            await moduleRepository.UpdateAsync(module, cancellationToken);
            return module;
        }
        catch (Exception exception)
        {
            return new FormModuleUnknownException(moduleId, exception);
        }
    }
}