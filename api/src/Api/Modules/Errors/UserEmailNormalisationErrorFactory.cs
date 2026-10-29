using Application.UserEmailNormalisation.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Modules.Errors;

public static class UserEmailNormalisationErrorFactory
{
    public static ObjectResult ToObjectResult(this UserEmailNormalisationException error)
    {
        return new ObjectResult(error.Message)
        {
            StatusCode = error switch
            {
                UserEmailNormalisationUnknownException => StatusCodes.Status500InternalServerError,
                _ => throw new NotImplementedException("User email normalisation exception handler not implemented")
            }
        };
    }
}