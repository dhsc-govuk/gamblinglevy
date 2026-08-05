using Application.Organisations.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Api.Modules.Errors;

public static class OrganisationErrorFactory
{
    public static ObjectResult ToObjectResult(this OrganisationException error)
    {
        if (error is OrganisationHasActiveUsersException activeUsersEx)
        {
            return new ObjectResult(new
            {
                error = "ORGANISATION_HAS_ACTIVE_USERS",
                message = activeUsersEx.Message,
                activeUsers = activeUsersEx.ActiveUsers.Select(u => new
                {
                    id = u.Id.Value,
                    firstName = u.Name.FirstName,
                    lastName = u.Name.LastName,
                    email = u.Email,
                }),
            })
            {
                StatusCode = StatusCodes.Status422UnprocessableEntity,
            };
        }

        // Must stay an early return: the switch below has no arm for this type, so removing this block
        // drops through to the NotImplementedException default and turns every bulk-import input error into a 500.
        if (error is OrganisationBulkImportFailedException bulkImportEx)
        {
            return new ObjectResult(new
            {
                error = bulkImportEx.Code,
                message = bulkImportEx.Message,
            })
            {
                StatusCode = StatusCodes.Status400BadRequest,
            };
        }

        return new ObjectResult(error.Message)
        {
            StatusCode = error switch
            {
                OrganisationAlreadyExistsException => StatusCodes.Status409Conflict,
                OrganisationDuplicateOnsCodeException => StatusCodes.Status409Conflict,
                OrganisationDoesNotExistException => StatusCodes.Status404NotFound,
                OrganisationAllocationNotFoundException => StatusCodes.Status404NotFound,
                OrganisationOnboardingStatusNotEditableException => StatusCodes.Status400BadRequest,
                OrganisationOnsCodeChangeNotConfirmedException => StatusCodes.Status400BadRequest,
                OrganisationCannotBeDeletedException => StatusCodes.Status422UnprocessableEntity,
                OrganisationCannotBeSuspendedException => StatusCodes.Status422UnprocessableEntity,
                OrganisationCannotBeReinstatedException => StatusCodes.Status422UnprocessableEntity,
                OrganisationHasSubmissionsException => StatusCodes.Status422UnprocessableEntity,
                OrganisationUnknownException => StatusCodes.Status500InternalServerError,
                _ => throw new NotImplementedException("Organisation exception handler does not implemented")
            }
        };
    }
}