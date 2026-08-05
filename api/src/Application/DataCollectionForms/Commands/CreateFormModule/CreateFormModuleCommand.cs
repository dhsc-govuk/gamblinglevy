using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.DataCollectionForms.Exceptions;
using Domain.DataCollectionForms;
using LanguageExt;
using MediatR;

namespace Application.DataCollectionForms.Commands.CreateFormModule;

public record CreateFormModuleCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public string? Code { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
}

public class CreateFormModuleCommandHandler(
    IFormModuleRepository moduleRepository,
    IFormModuleQueries moduleQueries,
    PermissionsService permissionsService)
    : IRequestHandler<CreateFormModuleCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        CreateFormModuleCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormModuleForbiddenException(FormModuleId.Empty(), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        CreateFormModuleCommand request,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        var code = request.Code!.Trim();

        var existing = await moduleQueries.GetByCodeAsync(code, cancellationToken);
        if (existing.IsSome)
        {
            return new FormModuleDuplicateCodeException(FormModuleId.Empty(), code);
        }

        var moduleId = FormModuleId.New();

        try
        {
            var activeModules = await moduleQueries.GetAllActiveAsync(cancellationToken);
            var displayOrder = activeModules.Count + 1;

            var module = FormModule.Create(
                moduleId,
                code,
                request.Name!,
                displayOrder,
                actorId,
                request.Description);

            return await moduleRepository.AddAsync(module, cancellationToken);
        }
        catch (Exception exception)
        {
            return new FormModuleUnknownException(moduleId, exception);
        }
    }
}