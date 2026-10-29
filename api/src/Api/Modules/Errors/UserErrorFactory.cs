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
                UserUnknownException => StatusCodes.Status500InternalServerError,
                UserOrganisationDoesNotExistException => StatusCodes.Status400BadRequest,
                UserLocationDoesNotExistException => StatusCodes.Status400BadRequest,
                UserArgumentException => StatusCodes.Status400BadRequest,
                CannotDeactivateSelfException => StatusCodes.Status422UnprocessableEntity,
                CannotRemoveLastActiveAdminException => StatusCodes.Status422UnprocessableEntity,
                CannotDeleteSelfException => StatusCodes.Status422UnprocessableEntity,
                UserRoleNotAssignableException => StatusCodes.Status422UnprocessableEntity,
                UserForbiddenException => StatusCodes.Status403Forbidden,
                PasswordLoginNotEnabledException => StatusCodes.Status422UnprocessableEntity,
                TemporaryPasswordEmailNotConfiguredException => StatusCodes.Status500InternalServerError,
                TemporaryPasswordEmailFailedException => StatusCodes.Status500InternalServerError,
                _ => throw new NotImplementedException("User exception handler does not implemented"),
            },
        };
    }
}