using Domain.Forms;

namespace Application.Forms.Exceptions;

public abstract class FormDesignerException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public class FormSectionNotFoundException(FormSectionId id)
    : FormDesignerException($"Form section with id {id} does not exist.")
{
    public FormSectionId SectionId { get; } = id;
}

public class FormQuestionNotFoundException(FormQuestionId id)
    : FormDesignerException($"Form question with id {id} does not exist.")
{
    public FormQuestionId QuestionId { get; } = id;
}

public class DuplicateQuestionCodeException(string code)
    : FormDesignerException($"A question with code \"{code}\" already exists.")
{
    public string Code { get; } = code;
}

public class FormSectionNotEmptyException(FormSectionId id)
    : FormDesignerException($"Form section {id} still contains questions. Move or delete them before deleting the section.")
{
    public FormSectionId SectionId { get; } = id;
}

public class InvalidConditionalReferenceException(string parentCode)
    : FormDesignerException($"Conditional parent question \"{parentCode}\" does not exist.")
{
    public string ParentCode { get; } = parentCode;
}

public class QuestionOptionsRequiredException(string code)
    : FormDesignerException($"Question \"{code}\" is a choice type and requires at least one option.")
{
    public string Code { get; } = code;
}

public class FormQuestionReferencedException(FormQuestionId id, string code)
    : FormDesignerException($"Question \"{code}\" is used by another question's conditional rule and cannot be deleted. Remove the dependent rule first.")
{
    public FormQuestionId QuestionId { get; } = id;
    public string Code { get; } = code;
}

public class FormDesignerForbiddenException(string message)
    : FormDesignerException(message);

public class FormDesignerUnknownException(Exception innerException)
    : FormDesignerException("An unexpected error occurred in the Form Designer.", innerException);