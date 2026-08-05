using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.DataCollectionForms.Exceptions;
using Domain.DataCollectionForms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.DataCollectionForms.Commands.UpdateFormModule;

public record UpdateFormModuleCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public Guid Id { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
}

public class UpdateFormModuleCommandHandler(
    IFormModuleRepository moduleRepository,
    IFormModuleQueries moduleQueries,
    PermissionsService permissionsService)
    : IRequestHandler<UpdateFormModuleCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        UpdateFormModuleCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormModuleForbiddenException(FormModuleId.From(request.Id), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        UpdateFormModuleCommand request,
        Domain.Users.UserId actorId,
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
            module.UpdateDetails(request.Name!, request.Description, actorId);
            await moduleRepository.UpdateAsync(module, cancellationToken);
            return module;
        }
        catch (Exception exception)
        {
            return new FormModuleUnknownException(moduleId, exception);
        }
    }
}