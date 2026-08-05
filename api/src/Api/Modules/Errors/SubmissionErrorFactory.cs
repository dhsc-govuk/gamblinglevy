using Application.Submissions.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Modules.Errors;

public static class SubmissionErrorFactory
{
    public static ObjectResult ToObjectResult(this SubmissionException error)
    {
        if (error is SubmissionAnswersInvalidException answersInvalid)
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

        // Funding-spend amount errors reuse the shared answer-invalid envelope so the frontend mapReportError and
        // its per-field routing need no change — codes are actual-{id} / other-spend / forecast-{id}-q{n} / etc.
        if (error is FundingSpendInvalidException fundingInvalid)
        {
            return new ObjectResult(new
            {
                error = "SUBMISSION_ANSWERS_INVALID",
                message = fundingInvalid.Message,
                answerErrors = fundingInvalid.Errors.Select(e => new { questionCode = e.QuestionCode, message = e.Message }),
            })
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity,
            };
        }

        // Strategic-feedback answer errors reuse the shared answer-invalid envelope (keyed by question code) so the
        // frontend mapReportError and its per-field routing need no change.
        if (error is StrategicFeedbackInvalidException feedbackInvalid)
        {
            return new ObjectResult(new
            {
                error = "SUBMISSION_ANSWERS_INVALID",
                message = feedbackInvalid.Message,
                answerErrors = feedbackInvalid.Errors.Select(e => new { questionCode = e.QuestionCode, message = e.Message }),
            })
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity,
            };
        }

        if (error is SubmissionCollectionNotOpenException notOpen)
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

        if (error is SubmissionActivitiesNotConfirmedException notConfirmed)
        {
            return new ObjectResult(new
            {
                error = "ACTIVITIES_NOT_CONFIRMED",
                message = notConfirmed.Message,
            })
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity,
            };
        }

        if (error is SubmissionNotReadyToSubmitException notReady)
        {
            return new ObjectResult(new
            {
                error = "REPORT_INCOMPLETE",
                message = notReady.Message,
            })
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity,
            };
        }

        return new ObjectResult(error.Message)
        {
            StatusCode = error switch
            {
                SubmissionCollectionNotEligibleException => StatusCodes.Status404NotFound,
                SubmissionDoesNotExistException => StatusCodes.Status404NotFound,
                SubmissionAlreadyExistsException => StatusCodes.Status409Conflict,
                SubmissionAlreadySubmittedException => StatusCodes.Status409Conflict,
                SubmissionReferenceConflictException => StatusCodes.Status409Conflict,
                SubmissionActivityNotReportableException => StatusCodes.Status422UnprocessableEntity,
                SubmissionForbiddenException => StatusCodes.Status403Forbidden,
                SubmissionUnknownException => StatusCodes.Status500InternalServerError,
                _ => throw new NotImplementedException("Submission exception handler not implemented")
            }
        };
    }
}