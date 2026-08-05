using Application.Activities.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Modules.Errors;

public static class ActivityErrorFactory
{
    public static ObjectResult ToObjectResult(this ActivityException error)
    {
        if (error is ActivityIncompleteException incomplete)
        {
            return new ObjectResult(new
            {
                error = "ACTIVITY_INCOMPLETE",
                message = incomplete.Message,
                missingFields = incomplete.MissingFields,
            })
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity,
            };
        }

        if (error is ActivityAnswersInvalidException answersInvalid)
        {
            return new ObjectResult(new
            {
                error = "ACTIVITY_ANSWERS_INVALID",
                message = answersInvalid.Message,
                answerErrors = answersInvalid.Errors.Select(e => new { questionCode = e.QuestionCode, message = e.Message }),
            })
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity,
            };
        }

        return new ObjectResult(error.Message)
        {
            StatusCode = error switch
            {
                ActivityDoesNotExistException => StatusCodes.Status404NotFound,
                ActivityForbiddenException => StatusCodes.Status403Forbidden,
                ActivityExpectedResultsInvalidException => StatusCodes.Status422UnprocessableEntity,
                ActivityNotEditableException => StatusCodes.Status422UnprocessableEntity,
                ActivityCannotBeDeletedException => StatusCodes.Status422UnprocessableEntity,
                ActivityConcurrencyConflictException => StatusCodes.Status409Conflict,
                ActivityUnknownException => StatusCodes.Status500InternalServerError,
                _ => throw new NotImplementedException("Activity exception handler not implemented")
            }
        };
    }
}