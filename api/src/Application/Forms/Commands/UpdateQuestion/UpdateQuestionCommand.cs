using Application.Common.Interfaces.Repositories;
using Application.Forms.Exceptions;
using Domain.Forms;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Forms.Commands.UpdateQuestion;

// Updates a question's details, section, options and active state. Code is immutable (it is the stable answer
// key), so it is not part of the request.
public record UpdateQuestionCommand : IRequest<Either<FormDesignerException, FormQuestion>>
{
    public Guid Id { get; init; }
    public Guid SectionId { get; init; }
    public string? Label { get; init; }
    public string? QuestionType { get; init; }
    public bool IsRequired { get; init; }
    public bool IsActive { get; init; }
    public string? Hint { get; init; }
    public string? HelpText { get; init; }
    public string? HelpTextSummary { get; init; }
    public string? ConditionalQuestionCode { get; init; }
    public string? ConditionalValue { get; init; }
    public IReadOnlyList<QuestionOptionInput> Options { get; init; } = [];
}

public class UpdateQuestionCommandHandler(
    IFormSectionRepository sectionRepository,
    IFormQuestionRepository questionRepository)
    : IRequestHandler<UpdateQuestionCommand, Either<FormDesignerException, FormQuestion>>
{
    public async Task<Either<FormDesignerException, FormQuestion>> Handle(
        UpdateQuestionCommand request,
        CancellationToken cancellationToken)
    {
        var questionId = new FormQuestionId(request.Id);
        var lookup = await questionRepository.GetByIdAsync(questionId, cancellationToken);
        if (lookup.IsNone)
        {
            return new FormQuestionNotFoundException(questionId);
        }

        var question = lookup.ValueUnsafe();

        var sectionId = new FormSectionId(request.SectionId);
        var sectionLookup = await sectionRepository.GetByIdAsync(sectionId, cancellationToken);
        if (sectionLookup.IsNone)
        {
            return new FormSectionNotFoundException(sectionId);
        }

        var questionType = QuestionType.From(request.QuestionType!);
        if (questionType.IsChoice && request.Options.Count == 0)
        {
            return new QuestionOptionsRequiredException(question.Code);
        }

        if (!string.IsNullOrWhiteSpace(request.ConditionalQuestionCode))
        {
            var parentCode = request.ConditionalQuestionCode.Trim();
            if (string.Equals(parentCode, question.Code, StringComparison.Ordinal))
            {
                return new InvalidConditionalReferenceException(parentCode);
            }

            var parent = await questionRepository.FindByCodeAsync(parentCode, cancellationToken);
            if (parent.IsNone)
            {
                return new InvalidConditionalReferenceException(parentCode);
            }
        }

        try
        {
            question.UpdateDetails(
                request.Label!,
                questionType,
                request.IsRequired,
                request.Hint,
                request.HelpText,
                request.HelpTextSummary,
                request.ConditionalQuestionCode,
                request.ConditionalValue);

            if (question.SectionId != sectionId)
            {
                question.MoveToSection(sectionId);
            }

            question.ClearOptions();
            foreach (var option in request.Options)
            {
                question.AddOption(FormQuestionOptionId.New(), option.Value, option.Label, option.DisplayOrder);
            }

            if (request.IsActive)
            {
                question.Activate();
            }
            else
            {
                question.Deactivate();
            }

            await questionRepository.SaveChangesAsync(cancellationToken);
            return question;
        }
        catch (Exception exception)
        {
            return new FormDesignerUnknownException(exception);
        }
    }
}