using Domain.DataCollectionForms;

namespace Application.DataCollectionForms.Exceptions;

public abstract class FormModuleException(FormModuleId id, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public FormModuleId FormModuleId { get; } = id;
}

public class FormModuleNotFoundException(FormModuleId id)
    : FormModuleException(id, $"Form module with id {id} does not exist.");

public class FormModuleDuplicateCodeException(FormModuleId id, string code)
    : FormModuleException(id, $"A form module with code \"{code}\" already exists.")
{
    public string Code { get; } = code;
}

public class FormModuleNotEditableException(FormModuleId id)
    : FormModuleException(id, $"Form module {id} is published and its structure can no longer be edited.");

public class FormModuleEmptyOnPublishException(FormModuleId id)
    : FormModuleException(id, $"Form module {id} cannot be published with no questions.");

public class DcFormSectionNotFoundException(FormModuleId id, DcFormSectionId sectionId)
    : FormModuleException(id, $"Section {sectionId} does not exist on form module {id}.")
{
    public DcFormSectionId SectionId { get; } = sectionId;
}

public class DcFormSectionNotEmptyException(FormModuleId id, DcFormSectionId sectionId)
    : FormModuleException(id, $"Section {sectionId} has questions and cannot be deleted.")
{
    public DcFormSectionId SectionId { get; } = sectionId;
}

public class DcFormQuestionNotFoundException(FormModuleId id, DcFormQuestionId questionId)
    : FormModuleException(id, $"Question {questionId} does not exist on form module {id}.")
{
    public DcFormQuestionId QuestionId { get; } = questionId;
}

public class DcFormQuestionDuplicateCodeException(FormModuleId id, string code)
    : FormModuleException(id, $"A question with code \"{code}\" already exists on form module {id}.")
{
    public string Code { get; } = code;
}

public class FormModuleForbiddenException(FormModuleId id, string message)
    : FormModuleException(id, message);

public class FormModuleUnknownException(FormModuleId id, Exception innerException)
    : FormModuleException(id, $"Unknown exception for the form module {id}", innerException);