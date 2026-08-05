using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.DataCollectionForms.Exceptions;
using Domain.DataCollectionForms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.DataCollectionForms.Commands.PublishFormModule;

public record PublishFormModuleCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public Guid Id { get; init; }
}

public class PublishFormModuleCommandHandler(
    IFormModuleRepository moduleRepository,
    IFormModuleQueries moduleQueries,
    PermissionsService permissionsService)
    : IRequestHandler<PublishFormModuleCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        PublishFormModuleCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormModuleForbiddenException(FormModuleId.From(request.Id), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        PublishFormModuleCommand request,
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

        if (module.Status.IsPublished)
        {
            return new FormModuleNotEditableException(moduleId);
        }

        if (!module.Questions.Any())
        {
            return new Application.DataCollectionForms.Exceptions.FormModuleEmptyOnPublishException(moduleId);
        }

        try
        {
            module.Publish(actorId);
            await moduleRepository.UpdateAsync(module, cancellationToken);
            return module;
        }
        catch (Domain.DataCollectionForms.FormModuleEmptyOnPublishException)
        {
            return new Application.DataCollectionForms.Exceptions.FormModuleEmptyOnPublishException(moduleId);
        }
        catch (Domain.DataCollectionForms.FormModuleAlreadyPublishedException)
        {
            return new FormModuleNotEditableException(moduleId);
        }
        catch (Exception exception)
        {
            return new FormModuleUnknownException(moduleId, exception);
        }
    }
}