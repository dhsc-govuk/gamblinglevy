namespace Domain.Activities;

// Defensive domain guards: thrown when a mutating operation violates an activity invariant. Application
// handlers pre-check and return a typed ActivityException, so these act as a last line of defence rather
// than the primary control-flow signal.
public class ActivityClosedException(ActivityId id, ActivityStatus status)
    : Exception($"Activity {id} is {status} and can no longer be edited.");

public class ActivityCategoryLockedException(ActivityId id)
    : Exception($"Activity {id} category and type cannot be changed once setup is complete.");

public class ActivityAlreadyDeletedException(ActivityId id)
    : Exception($"Activity {id} is already deleted.");

public class ActivityNotDeletedException(ActivityId id)
    : Exception($"Activity {id} is not deleted and cannot be restored.");