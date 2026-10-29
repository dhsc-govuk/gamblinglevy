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

public class FormModuleNotPublishedException(FormModuleId id)
    : FormModuleException(id, $"Form module {id} is not published, so there is no published form to start a draft from.");

public class FormModuleVersionConflictException(FormModuleId id)
    : FormModuleException(id, $"Form module {id} was published by someone else at the same time. Reload it and try again.");

public class FormModuleArchivedException(FormModuleId id)
    : FormModuleException(id, $"Form module {id} is archived and cannot be changed.");

public class FormModuleVersionNotFoundException(FormModuleId id, int versionNumber)
    : FormModuleException(id, $"Version {versionNumber} of form module {id} does not exist.")
{
    public int VersionNumber { get; } = versionNumber;
}

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

// BLUM-9105: a code an earlier published version used, offered for a new question.
public class DcFormQuestionCodeRetiredException(FormModuleId id, string code)
    : FormModuleException(id, $"The code \"{code}\" was used by an earlier version of this form. Choose a different code.")
{
    public string Code { get; } = code;
}

// BLUM-9105: an edit that changes or removes a published option's stored value.
public class DcFormQuestionOptionValueFixedException(FormModuleId id, string value)
    : FormModuleException(id, $"The option \"{value}\" has been published, so its value cannot be changed or removed. You can change its label.")
{
    public string Value { get; } = value;
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