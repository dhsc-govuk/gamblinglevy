using Application.Users.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Modules.Errors;

public static class UserErrorFactory
{
    public static ObjectResult ToObjectResult(this UserException error)
    {
        return new ObjectResult(error.Message)
        {
            StatusCode = error switch
            {
                UserExistsException => StatusCodes.Status409Conflict,
                DuplicateEmailException => StatusCodes.Status409Conflict,
                UserAlreadyDeactivatedException => StatusCodes.Status409Conflict,
                UserAlreadyActiveException => StatusCodes.Status409Conflict,
                UserDoesNotExistException => StatusCodes.Status404NotFound,
                UserWrongRoleException => StatusCodes.Status400BadRequest,
                UserUnknownException => StatusCodes.Status500InternalServerError,
                UserOrganisationDoesNotExistException => StatusCodes.Status400BadRequest,
                UserLocationDoesNotExistException => StatusCodes.Status400BadRequest,
                UserArgumentException => StatusCodes.Status400BadRequest,
                CannotDeactivateSelfException => StatusCodes.Status422UnprocessableEntity,
                CannotDeactivateOnlyAdminException => StatusCodes.Status422UnprocessableEntity,
                CannotDeactivateOnlyOrgAdminException => StatusCodes.Status422UnprocessableEntity,
                CannotDeleteSelfException => StatusCodes.Status422UnprocessableEntity,
                CannotDeleteOnlyAdminException => StatusCodes.Status422UnprocessableEntity,
                UserRoleNotAssignableException => StatusCodes.Status422UnprocessableEntity,
                UserForbiddenException => StatusCodes.Status403Forbidden,
                _ => throw new NotImplementedException("User exception handler does not implemented"),
            },
        };
    }
}