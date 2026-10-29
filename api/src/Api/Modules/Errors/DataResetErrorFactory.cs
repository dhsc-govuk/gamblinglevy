using Application.DataReset.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Modules.Errors;

public static class DataResetErrorFactory
{
    public static ObjectResult ToObjectResult(this DataResetException error)
    {
        return new ObjectResult(error.Message)
        {
            StatusCode = error switch
            {
                DataResetUnknownException => StatusCodes.Status500InternalServerError,
                MfaDataClearUnknownException => StatusCodes.Status500InternalServerError,
                _ => throw new NotImplementedException("Data reset exception handler not implemented")
            }
        };
    }
}