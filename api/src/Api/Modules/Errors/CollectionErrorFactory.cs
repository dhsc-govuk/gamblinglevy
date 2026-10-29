using Application.Collections.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Modules.Errors;

public static class CollectionErrorFactory
{
    public static ObjectResult ToObjectResult(this CollectionException error)
    {
        // The one refusal with an error code (BLUM-9084), so the collection wizard can put it on the Forms field. The
        // other refusals stay plain text: the client tells them apart by status alone (every 409 is "name taken").
        if (error is CollectionFormsLockedException)
        {
            return new ObjectResult(new
            {
                error = CollectionFormsLockedException.ErrorCode,
                message = error.Message,
            })
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity,
            };
        }

        return new ObjectResult(error.Message)
        {
            StatusCode = error switch
            {
                CollectionDoesNotExistException => StatusCodes.Status404NotFound,
                CollectionForbiddenException => StatusCodes.Status403Forbidden,
                CollectionNameTakenException => StatusCodes.Status409Conflict,
                CollectionNotEditableException => StatusCodes.Status422UnprocessableEntity,
                CollectionInvalidTransitionException => StatusCodes.Status422UnprocessableEntity,
                CollectionInvalidWindowException => StatusCodes.Status422UnprocessableEntity,
                CollectionQuarterRequiredException => StatusCodes.Status422UnprocessableEntity,
                CollectionLocalAuthorityInvalidException => StatusCodes.Status422UnprocessableEntity,
                CollectionFormModuleInvalidException => StatusCodes.Status422UnprocessableEntity,
                CollectionUnknownException => StatusCodes.Status500InternalServerError,
                _ => throw new NotImplementedException("Collection exception handler not implemented")
            }
        };
    }
}