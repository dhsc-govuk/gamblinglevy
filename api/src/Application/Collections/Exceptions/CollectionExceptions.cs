using Domain.Collections;

namespace Application.Collections.Exceptions;

public abstract class CollectionException(CollectionId id, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public CollectionId CollectionId { get; } = id;
}

public class CollectionDoesNotExistException(CollectionId id)
    : CollectionException(id, $"Collection with id {id} does not exist.");

public class CollectionNameTakenException(CollectionId id, string name)
    : CollectionException(id, $"A collection named \"{name}\" already exists.")
{
    public string Name { get; } = name;
}

public class CollectionNotEditableException(CollectionId id, string currentStatus)
    : CollectionException(id, $"Collection {id} is {currentStatus} and can no longer be edited.")
{
    public string CurrentStatus { get; } = currentStatus;
}

public class CollectionInvalidTransitionException(CollectionId id, string from, string to)
    : CollectionException(id, $"Collection {id} cannot transition from {from} to {to}.")
{
    public string From { get; } = from;

    public string To { get; } = to;
}

public class CollectionInvalidWindowException(CollectionId id, string message)
    : CollectionException(id, message);

public class CollectionQuarterRequiredException(CollectionId id)
    : CollectionException(id, $"Collection {id} is a quarterly report and requires a quarter between 1 and 4.");

public class CollectionLocalAuthorityInvalidException(CollectionId id)
    : CollectionException(id, $"Collection {id} references one or more Local Authorities that do not exist.");

public class CollectionFormModuleInvalidException(CollectionId id)
    : CollectionException(id, $"Collection {id} references one or more form modules that do not exist or are not available.");

public class CollectionForbiddenException(CollectionId id, string message)
    : CollectionException(id, message);

public class CollectionUnknownException(CollectionId id, Exception innerException)
    : CollectionException(id, $"Unknown exception for the collection {id}", innerException);