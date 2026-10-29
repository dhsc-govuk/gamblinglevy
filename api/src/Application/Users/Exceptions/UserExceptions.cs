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

public class UserUnknownException(UserId id, Exception innerException)
    : UserException(id, $"Unknown error for user with id {id}", innerException);

public class UserOrganisationDoesNotExistException(UserId id, OrganisationId organisationId)
    : UserException(id, $"Organisation with id {organisationId} does not exist");

public class UserLocationDoesNotExistException(UserId id, LocationId locationId)
    : UserException(id, $"Location with id {locationId} does not exist");

public class UserArgumentException(UserId id, string message) : UserException(id, message);

public class CannotDeactivateSelfException(UserId id)
    : UserException(id, $"User with id {id} cannot deactivate their own account");

public class CannotDeleteSelfException(UserId id)
    : UserException(id, $"User with id {id} cannot delete their own account");

public class UserAlreadyDeactivatedException(UserId id)
    : UserException(id, $"User with id {id} is already deactivated");

public class UserAlreadyActiveException(UserId id)
    : UserException(id, $"User with id {id} is already active");

// Refused by LastActiveAdminGuard: the change would leave DHSC with no active admin (organisationId null), or an
// LA with no active LA Admin. One refusal for deactivating, deleting, demoting and moving, whichever route asked.
public class CannotRemoveLastActiveAdminException(UserId id, OrganisationId? organisationId)
    : UserException(
        id,
        organisationId is null
            ? $"User with id {id} is the only active departmental admin and cannot be deactivated, deleted or demoted"
            : $"User with id {id} is the only active organisation admin for organisation {organisationId} and cannot be deactivated, deleted, demoted or moved")
{
    public OrganisationId? OrganisationId { get; } = organisationId;
}

public class UserForbiddenException(UserId id)
    : UserException(id, $"User with id {id} cannot be accessed by the current user");

public class UserRoleNotAssignableException(UserId id, string targetRole, string actorRole)
    : UserException(id, $"Role \"{targetRole}\" cannot be assigned by a user with role \"{actorRole}\"");

public class PasswordLoginNotEnabledException(UserId id)
    : UserException(id, $"User with id {id} does not have password login enabled");

// Refused before Entra was touched: ITemporaryPasswordEmailService.IsConfigured was false, so there would be no
// way to tell the user their new temporary password once their credential was replaced and every session
// revoked. See ITemporaryPasswordEmailService for why this is a hard refusal rather than a logged warning.
public class TemporaryPasswordEmailNotConfiguredException(UserId id)
    : UserException(id, $"Cannot change the password for user with id {id}: temporary password email is not configured");

// The send was attempted — after Entra was already changed and every session already revoked — and failed.
// Surfaced to the caller rather than logged and swallowed, because this email is the only way the user finds
// out how to sign in again.
public class TemporaryPasswordEmailFailedException(UserId id, Exception innerException)
    : UserException(id, $"Temporary password email failed to send for user with id {id}", innerException);