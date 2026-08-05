using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.DataCollectionForms.Exceptions;
using Domain.DataCollectionForms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.DataCollectionForms.Commands.DeleteFormQuestion;

// Removes a question (and its owned options) from a forms-builder module. Admin-only.
public record DeleteFormQuestionCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public Guid FormModuleId { get; init; }
    public Guid Id { get; init; }
}

public class DeleteFormQuestionCommandHandler(
    IFormModuleQueries formModuleQueries,
    IFormModuleRepository formModuleRepository,
    PermissionsService permissionsService)
    : IRequestHandler<DeleteFormQuestionCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        DeleteFormQuestionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            _ => HandleWithActor(request, cancellationToken),
            error => new FormModuleForbiddenException(Domain.DataCollectionForms.FormModuleId.Empty(), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        DeleteFormQuestionCommand request,
        CancellationToken cancellationToken)
    {
        var moduleId = new FormModuleId(request.FormModuleId);
        var lookup = await formModuleQueries.GetByIdWithChildrenAsync(moduleId, cancellationToken);
        if (lookup.IsNone)
        {
            return new FormModuleNotFoundException(moduleId);
        }

        var module = lookup.ValueUnsafe();
        if (module.Status.IsPublished)
        {
            return new FormModuleNotEditableException(module.Id);
        }

        var questionId = new DcFormQuestionId(request.Id);
        if (module.Questions.All(q => q.Id != questionId))
        {
            return new DcFormQuestionNotFoundException(moduleId, questionId);
        }

        try
        {
            module.RemoveQuestion(questionId);
            await formModuleRepository.UpdateAsync(module, cancellationToken);
            return module;
        }
        catch (Exception exception)
        {
            return new FormModuleUnknownException(moduleId, exception);
        }
    }
}