using Domain.Locations;
using Domain.Organisations;
using Domain.Users;

namespace Application.Users.Exceptions;

public abstract class UserException(UserId id, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public UserId UserId { get; } = id;
}

public class UserExistsException(UserId id, string email)
    : UserException(id, $"User with email {email} already exists under id {id}");

public class DuplicateEmailException(UserId id, string email)
    : UserException(id, $"User with email {email} already exists under id {id}");

public class UserDoesNotExistException(UserId id) : UserException(id, $"User with id {id} does not exist");

public class UserWrongRoleException(UserId id, string requiredRole)
    : UserException(id, $"User with id {id} does not belong to role {requiredRole}");

public class UserUnknownException(UserId id, Exception innerException)
    : UserException(id, $"Unknown error for user with id {id}", innerException);

public class UserOrganisationDoesNotExistException(UserId id, OrganisationId organisationId)
    : UserException(id, $"Organisation with id {organisationId} does not exist");

public class UserLocationDoesNotExistException(UserId id, LocationId locationId)
    : UserException(id, $"Location with id {locationId} does not exist");

public class UserArgumentException(UserId id, string message) : UserException(id, message);

public class CannotDeactivateSelfException(UserId id)
    : UserException(id, $"User with id {id} cannot deactivate their own account");

public class CannotDeactivateOnlyAdminException(UserId id)
    : UserException(id, $"User with id {id} is the only active departmental admin and cannot be deactivated");

public class CannotDeleteSelfException(UserId id)
    : UserException(id, $"User with id {id} cannot delete their own account");

public class CannotDeleteOnlyAdminException(UserId id)
    : UserException(id, $"User with id {id} is the only active departmental admin and cannot be deleted");

public class UserAlreadyDeactivatedException(UserId id)
    : UserException(id, $"User with id {id} is already deactivated");

public class UserAlreadyActiveException(UserId id)
    : UserException(id, $"User with id {id} is already active");

public class CannotDeactivateOnlyOrgAdminException(UserId id, OrganisationId organisationId)
    : UserException(id, $"User with id {id} is the only active organisation admin for organisation {organisationId} and cannot be deactivated, deleted, or demoted");

public class UserForbiddenException(UserId id)
    : UserException(id, $"User with id {id} cannot be accessed by the current user");

public class UserRoleNotAssignableException(UserId id, string targetRole, string actorRole)
    : UserException(id, $"Role \"{targetRole}\" cannot be assigned by a user with role \"{actorRole}\"");