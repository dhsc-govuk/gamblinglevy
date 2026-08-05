using Domain.Organisations;
using Domain.Users;

namespace Application.Organisations.Exceptions;

public abstract class OrganisationException(OrganisationId id, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public OrganisationId OrganisationId { get; } = id;
}

public class OrganisationAlreadyExistsException(OrganisationId id, string name)
    : OrganisationException(id, $"Organisation with name {name} already exists under id {id}");

public class OrganisationDuplicateOnsCodeException(OrganisationId id, string onsCode)
    : OrganisationException(id, $"Organisation with ONS code {onsCode} already exists under id {id}");

public class OrganisationDoesNotExistException(OrganisationId id)
    : OrganisationException(id, $"Organisation with id {id} does not exist");

public class OrganisationCannotBeDeletedException(OrganisationId id, string currentStatus)
    : OrganisationException(id, $"Organisation {id} cannot be deleted while in status '{currentStatus}'. Only invited or suspended accounts may be deleted.");

public class OrganisationCannotBeSuspendedException(OrganisationId id, string currentStatus)
    : OrganisationException(id, $"Organisation {id} cannot be suspended while in status '{currentStatus}'. Only active or invited accounts may be suspended.");

public class OrganisationCannotBeReinstatedException(OrganisationId id, string currentStatus)
    : OrganisationException(id, $"Organisation {id} cannot be reinstated while in status '{currentStatus}'. Only suspended accounts may be reinstated.");

public class OrganisationHasSubmissionsException(OrganisationId id)
    : OrganisationException(id, $"Organisation {id} has submission data. Contact a platform architect before deleting.");

public class OrganisationHasActiveUsersException(OrganisationId id, IReadOnlyList<User> activeUsers)
    : OrganisationException(id, $"Organisation {id} has {activeUsers.Count} active user(s) who will lose access.")
{
    public IReadOnlyList<User> ActiveUsers { get; } = activeUsers;
}

public class OrganisationOnboardingStatusNotEditableException(OrganisationId id)
    : OrganisationException(id, $"Organisation {id} onboarding_status cannot be edited via this endpoint; it is managed by system events only.");

public class OrganisationOnsCodeChangeNotConfirmedException(OrganisationId id)
    : OrganisationException(id, $"Organisation {id} ONS code change requires explicit confirmation as it affects finance export linkage.");

public class OrganisationAllocationNotFoundException(OrganisationId id)
    : OrganisationException(id, $"Organisation {id} has no existing funding allocation to amend.");

// Message carries user-facing copy verbatim; Code is the stable envelope key the client and tests match on.
public class OrganisationBulkImportFailedException(OrganisationId id, string code, string message)
    : OrganisationException(id, message)
{
    public string Code { get; } = code;
}

public class OrganisationUnknownException(OrganisationId id, Exception innerException)
    : OrganisationException(id, $"Unknown exception for the organisation {id}", innerException);