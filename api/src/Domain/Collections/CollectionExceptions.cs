namespace Domain.Collections;

// Defensive domain guards: thrown when a mutating operation violates a collection invariant. Application
// handlers pre-check and return a typed CollectionException, so these act as a last line of defence rather
// than the primary control-flow signal.
public class CollectionAlreadyDeletedException(CollectionId id)
    : Exception($"Collection {id} is already deleted.");

public class CollectionNotDeletedException(CollectionId id)
    : Exception($"Collection {id} is not deleted and cannot be restored.");

public class CollectionNotEditableException(CollectionId id, CollectionStatus status)
    : Exception($"Collection {id} is {status} and can no longer be edited.");

public class CollectionInvalidWindowException(string message) : Exception(message);

public class CollectionQuarterRequiredException()
    : Exception("A quarter (1-4) is required for a quarterly report collection.");