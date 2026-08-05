using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Forms.Exceptions;
using Domain.Forms;
using LanguageExt;
using MediatR;

namespace Application.Forms.Commands.CreateQuestion;

public record CreateQuestionCommand : IRequest<Either<FormDesignerException, FormQuestion>>
{
    public Guid SectionId { get; init; }
    public string? Code { get; init; }
    public string? Label { get; init; }
    public string? QuestionType { get; init; }
    public bool IsRequired { get; init; }
    public string? Hint { get; init; }
    public string? HelpText { get; init; }
    public string? HelpTextSummary { get; init; }
    public string? ConditionalQuestionCode { get; init; }
    public string? ConditionalValue { get; init; }
    public IReadOnlyList<QuestionOptionInput> Options { get; init; } = [];
}

public class CreateQuestionCommandHandler(
    IFormSectionRepository sectionRepository,
    IFormQuestionRepository questionRepository,
    PermissionsService permissionsService)
    : IRequestHandler<CreateQuestionCommand, Either<FormDesignerException, FormQuestion>>
{
    public async Task<Either<FormDesignerException, FormQuestion>> Handle(
        CreateQuestionCommand request,
        CancellationToken cancellationToken)
    {
        return await permissionsService.GetUserId().MatchAsync<Either<FormDesignerException, FormQuestion>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            error => new FormDesignerForbiddenException(error.Message));
    }

    private async Task<Either<FormDesignerException, FormQuestion>> HandleWithActor(
        CreateQuestionCommand request,
        Domain.Users.UserId actorId,
        CancellationToken cancellationToken)
    {
        var sectionId = new FormSectionId(request.SectionId);
        var sectionLookup = await sectionRepository.GetByIdAsync(sectionId, cancellationToken);
        if (sectionLookup.IsNone)
        {
            return new FormSectionNotFoundException(sectionId);
        }

        var code = request.Code!.Trim();
        var existing = await questionRepository.FindByCodeAsync(code, cancellationToken);
        if (existing.IsSome)
        {
            return new DuplicateQuestionCodeException(code);
        }

        var questionType = QuestionType.From(request.QuestionType!);
        if (questionType.IsChoice && request.Options.Count == 0)
        {
            return new QuestionOptionsRequiredException(code);
        }

        if (!string.IsNullOrWhiteSpace(request.ConditionalQuestionCode))
        {
            var parent = await questionRepository.FindByCodeAsync(request.ConditionalQuestionCode.Trim(), cancellationToken);
            if (parent.IsNone)
            {
                return new InvalidConditionalReferenceException(request.ConditionalQuestionCode.Trim());
            }
        }

        try
        {
            var nextOrder = await questionRepository.GetMaxDisplayOrderForSectionAsync(sectionId, cancellationToken) + 1;
            var question = FormQuestion.Create(
                FormQuestionId.New(),
                sectionId,
                code,
                request.Label!,
                questionType,
                nextOrder,
                actorId,
                request.IsRequired,
                request.Hint,
                request.HelpText,
                request.HelpTextSummary,
                request.ConditionalQuestionCode,
                request.ConditionalValue);

            foreach (var option in request.Options)
            {
                question.AddOption(FormQuestionOptionId.New(), option.Value, option.Label, option.DisplayOrder);
            }

            return await questionRepository.AddAsync(question, cancellationToken);
        }
        catch (Exception exception)
        {
            return new FormDesignerUnknownException(exception);
        }
    }
}