using Application.Submissions.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Modules.Errors;

public static class SubmissionErrorFactory
{
    private const int MaxOfflineErrorsListed = 100;

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

        // Offline report upload (BLUM-8310): the file as a whole was refused. The message is fixed GDS copy the page
        // shows under the file input; the OFFLINE_* code tells the client which kind of problem it was.
        if (error is OfflineUploadRejectedException rejected)
        {
            return new ObjectResult(new
            {
                error = rejected.Code,
                message = rejected.Message,
            })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            };
        }

        // The file was read but its answers have problems. Every error carries its spreadsheet row, column letter and
        // question number; the list is capped (a file with thousands of errors would otherwise make a page nobody
        // can use) while the annotated file — the person's own file with each row's problems written into it —
        // carries all of them. It travels as base64 so the bytes (BOM, line endings) are exactly what we wrote.
        if (error is OfflineUploadInvalidException invalid)
        {
            return new ObjectResult(new
            {
                error = "OFFLINE_UPLOAD_INVALID",
                message = invalid.Message,
                moduleCode = invalid.ModuleCode,
                errorCount = invalid.Errors.Count,
                errors = invalid.Errors.Take(MaxOfflineErrorsListed).Select(e => new
                {
                    row = e.Row,
                    column = e.ColumnLetter,
                    question = e.QuestionNumber is { } number ? $"Q{number}" : null,
                    message = e.Message,
                }),
                annotatedFile = new
                {
                    fileName = invalid.AnnotatedFileName,
                    contentBase64 = Convert.ToBase64String(invalid.AnnotatedFile),
                },
            })
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity,
            };
        }

        if (error is OfflineSectionNotInReportException sectionNotInReport)
        {
            return new ObjectResult(new
            {
                error = "OFFLINE_SECTION_NOT_IN_REPORT",
                message = sectionNotInReport.Message,
            })
            {
                StatusCode = StatusCodes.Status404NotFound,
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

        if (error is SubmissionNotSubmittedException notSubmitted)
        {
            return new ObjectResult(new
            {
                error = "REPORT_NOT_SUBMITTED",
                message = notSubmitted.Message,
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