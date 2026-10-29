using Application.MaturityAssessments.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Modules.Errors;

// The envelope strings are deliberately the quarterly report's (SUBMISSION_ANSWERS_INVALID, REPORT_NOT_OPEN,
// REPORT_INCOMPLETE): client/src/views/Reports/messages.ts::mapReportError matches those literals to route
// per-field errors and the "window closed" copy, and the maturity screens reuse it unchanged.
public static class MaturityAssessmentErrorFactory
{
    public static ObjectResult ToObjectResult(this MaturityAssessmentException error)
    {
        if (error is MaturityAnswersInvalidException answersInvalid)
        {
            return new ObjectResult(new
            {
                error = "SUBMISSION_ANSWERS_INVALID",
                message = answersInvalid.Message,
                answerErrors = answersInvalid.Errors.Select(e => new { questionCode = e.QuestionCode, message = e.Message }),
            })
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity,
            };
        }

        if (error is MaturityAssessmentNotOpenException notOpen)
        {
            return new ObjectResult(new
            {
                error = "REPORT_NOT_OPEN",
                message = notOpen.Message,
            })
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity,
            };
        }

        if (error is MaturityAssessmentIncompleteException incomplete)
        {
            return new ObjectResult(new
            {
                error = "REPORT_INCOMPLETE",
                message = incomplete.Message,
            })
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity,
            };
        }

        return new ObjectResult(error.Message)
        {
            StatusCode = error switch
            {
                MaturityAssessmentCollectionNotEligibleException => StatusCodes.Status404NotFound,
                MaturityAssessmentDoesNotExistException => StatusCodes.Status404NotFound,
                MaturityAssessmentSectionUnknownException => StatusCodes.Status404NotFound,
                MaturityAssessmentPageUnknownException => StatusCodes.Status404NotFound,
                MaturityAssessmentAlreadyExistsException => StatusCodes.Status409Conflict,
                MaturityAssessmentAlreadySubmittedException => StatusCodes.Status409Conflict,
                MaturityAssessmentReferenceConflictException => StatusCodes.Status409Conflict,
                MaturityAssessmentForbiddenException => StatusCodes.Status403Forbidden,
                MaturityAssessmentUnknownException => StatusCodes.Status500InternalServerError,
                _ => throw new NotImplementedException("Maturity assessment exception handler not implemented")
            }
        };
    }
}