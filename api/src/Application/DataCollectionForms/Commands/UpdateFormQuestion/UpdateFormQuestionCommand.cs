using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.DataCollectionForms.Exceptions;
using Domain.DataCollectionForms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.DataCollectionForms.Commands.UpdateFormQuestion;

// Updates a question's details, section and options on a forms-builder module. Code is immutable (it is the
// stable answer key), so it is not part of the request. Admin-only.
public record UpdateFormQuestionCommand : IRequest<Either<FormModuleException, FormModule>>
{
    public Guid FormModuleId { get; init; }
    public Guid Id { get; init; }
    public Guid? SectionId { get; init; }
    public string? Code { get; init; }
    public string? Label { get; init; }
    public string? QuestionType { get; init; }
    public bool IsRequired { get; init; }
    public string? Hint { get; init; }
    public string? HelpText { get; init; }
    public IReadOnlyList<QuestionOptionInput> Options { get; init; } = [];
}

public class UpdateFormQuestionCommandHandler(
    IFormModuleQueries formModuleQueries,
    IFormModuleRepository formModuleRepository,
    PermissionsService permissionsService)
    : IRequestHandler<UpdateFormQuestionCommand, Either<FormModuleException, FormModule>>
{
    public async Task<Either<FormModuleException, FormModule>> Handle(
        UpdateFormQuestionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormModuleException, FormModule>>(
            _ => HandleWithActor(request, cancellationToken),
            error => new FormModuleForbiddenException(Domain.DataCollectionForms.FormModuleId.Empty(), error.Message));
    }

    private async Task<Either<FormModuleException, FormModule>> HandleWithActor(
        UpdateFormQuestionCommand request,
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
        var question = module.Questions.FirstOrDefault(q => q.Id == questionId);
        if (question is null)
        {
            return new DcFormQuestionNotFoundException(moduleId, questionId);
        }

        try
        {
            var questionType = Domain.Forms.QuestionType.From(request.QuestionType!);

            question.UpdateDetails(
                request.Label!,
                questionType,
                request.IsRequired,
                request.Hint,
                request.HelpText);

            question.SetSection(request.SectionId.HasValue ? new DcFormSectionId(request.SectionId.Value) : null);

            question.ClearOptions();
            if (questionType.IsChoice)
            {
                for (var idx = 0; idx < request.Options.Count; idx++)
                {
                    var option = request.Options[idx];
                    question.AddOption(DcFormQuestionOptionId.New(), option.Value, option.Label, idx + 1);
                }
            }

            await formModuleRepository.UpdateAsync(module, cancellationToken);
            return module;
        }
        catch (Exception exception)
        {
            return new FormModuleUnknownException(moduleId, exception);
        }
    }
}