using Application.Collections.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Modules.Errors;

public static class CollectionErrorFactory
{
    public static ObjectResult ToObjectResult(this CollectionException error)
    {
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