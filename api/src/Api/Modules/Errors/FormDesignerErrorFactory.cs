using Application.Forms.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Modules.Errors;

public static class FormDesignerErrorFactory
{
    public static ObjectResult ToObjectResult(this FormDesignerException error)
    {
        return new ObjectResult(new
        {
            error = ErrorCode(error),
            message = error.Message,
        })
        {
            StatusCode = error switch
            {
                FormSectionNotFoundException => StatusCodes.Status404NotFound,
                FormQuestionNotFoundException => StatusCodes.Status404NotFound,
                DuplicateQuestionCodeException => StatusCodes.Status409Conflict,
                FormQuestionReferencedException => StatusCodes.Status409Conflict,
                FormSectionNotEmptyException => StatusCodes.Status409Conflict,
                QuestionOptionsRequiredException => StatusCodes.Status422UnprocessableEntity,
                InvalidConditionalReferenceException => StatusCodes.Status422UnprocessableEntity,
                FormDesignerForbiddenException => StatusCodes.Status403Forbidden,
                FormDesignerUnknownException => StatusCodes.Status500InternalServerError,
                _ => throw new NotImplementedException("Form Designer exception handler not implemented")
            }
        };
    }

    private static string ErrorCode(FormDesignerException error) => error switch
    {
        FormSectionNotFoundException => "SECTION_NOT_FOUND",
        FormQuestionNotFoundException => "QUESTION_NOT_FOUND",
        DuplicateQuestionCodeException => "DUPLICATE_CODE",
        FormQuestionReferencedException => "QUESTION_REFERENCED",
        FormSectionNotEmptyException => "SECTION_NOT_EMPTY",
        QuestionOptionsRequiredException => "OPTIONS_REQUIRED",
        InvalidConditionalReferenceException => "INVALID_CONDITIONAL",
        FormDesignerForbiddenException => "FORBIDDEN",
        _ => "FORM_DESIGNER_ERROR",
    };
}