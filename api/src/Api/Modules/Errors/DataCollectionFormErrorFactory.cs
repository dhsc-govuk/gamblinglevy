using Application.DataCollectionForms.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Modules.Errors;

public static class DataCollectionFormErrorFactory
{
    public static ObjectResult ToObjectResult(this FormModuleException error)
    {
        return new ObjectResult(new
        {
            error = ErrorCode(error),
            message = error.Message,
        })
        {
            StatusCode = error switch
            {
                FormModuleNotFoundException => StatusCodes.Status404NotFound,
                DcFormSectionNotFoundException => StatusCodes.Status404NotFound,
                DcFormQuestionNotFoundException => StatusCodes.Status404NotFound,
                FormModuleDuplicateCodeException => StatusCodes.Status409Conflict,
                DcFormQuestionDuplicateCodeException => StatusCodes.Status409Conflict,
                DcFormQuestionCodeRetiredException => StatusCodes.Status409Conflict,
                DcFormQuestionOptionValueFixedException => StatusCodes.Status422UnprocessableEntity,
                FormModuleNotEditableException => StatusCodes.Status422UnprocessableEntity,
                FormModuleEmptyOnPublishException => StatusCodes.Status422UnprocessableEntity,
                FormModuleNotPublishedException => StatusCodes.Status422UnprocessableEntity,
                FormModuleVersionConflictException => StatusCodes.Status409Conflict,
                FormModuleVersionNotFoundException => StatusCodes.Status404NotFound,
                FormModuleArchivedException => StatusCodes.Status422UnprocessableEntity,
                DcFormSectionNotEmptyException => StatusCodes.Status422UnprocessableEntity,
                FormModuleForbiddenException => StatusCodes.Status403Forbidden,
                FormModuleUnknownException => StatusCodes.Status500InternalServerError,
                _ => throw new NotImplementedException("Data collection form exception handler not implemented")
            }
        };
    }

    private static string ErrorCode(FormModuleException error) => error switch
    {
        FormModuleNotFoundException => "MODULE_NOT_FOUND",
        DcFormSectionNotFoundException => "SECTION_NOT_FOUND",
        DcFormQuestionNotFoundException => "QUESTION_NOT_FOUND",
        FormModuleDuplicateCodeException => "DUPLICATE_MODULE_CODE",
        DcFormQuestionDuplicateCodeException => "DUPLICATE_QUESTION_CODE",
        DcFormQuestionCodeRetiredException => "QUESTION_CODE_RETIRED",
        DcFormQuestionOptionValueFixedException => "OPTION_VALUE_FIXED",
        FormModuleNotEditableException => "MODULE_NOT_EDITABLE",
        FormModuleEmptyOnPublishException => "MODULE_EMPTY_ON_PUBLISH",
        FormModuleNotPublishedException => "MODULE_NOT_PUBLISHED",
        FormModuleVersionConflictException => "MODULE_VERSION_CONFLICT",
        FormModuleVersionNotFoundException => "VERSION_NOT_FOUND",
        FormModuleArchivedException => "MODULE_ARCHIVED",
        DcFormSectionNotEmptyException => "SECTION_NOT_EMPTY",
        FormModuleForbiddenException => "FORBIDDEN",
        _ => "FORM_MODULE_ERROR",
    };
}