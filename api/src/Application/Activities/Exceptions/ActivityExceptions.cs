using Application.Forms.Answers;
using Domain.Activities;

namespace Application.Activities.Exceptions;

public abstract class ActivityException(ActivityId id, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public ActivityId ActivityId { get; } = id;
}

public class ActivityDoesNotExistException(ActivityId id)
    : ActivityException(id, $"Activity with id {id} does not exist.");

public class ActivityNotEditableException(ActivityId id, string currentStatus)
    : ActivityException(id, $"Activity {id} is {currentStatus} and can no longer be edited.")
{
    public string CurrentStatus { get; } = currentStatus;
}

public class ActivityCannotBeDeletedException(ActivityId id, string currentStatus)
    : ActivityException(id, $"Activity {id} is {currentStatus} and cannot be deleted. Only draft records can be deleted.")
{
    public string CurrentStatus { get; } = currentStatus;
}

public class ActivityIncompleteException(ActivityId id, IReadOnlyList<string> missingFields)
    : ActivityException(id, $"Activity {id} is missing required fields: {string.Join(", ", missingFields)}.")
{
    public IReadOnlyList<string> MissingFields { get; } = missingFields;
}

public class ActivityConcurrencyConflictException(ActivityId id)
    : ActivityException(id, $"Activity {id} was changed or removed by another user. Refresh and try again.");

public class ActivityAnswersInvalidException(ActivityId id, IReadOnlyList<AnswerValidationError> errors)
    : ActivityException(id, $"Activity {id} has invalid answers to one or more questions.")
{
    public IReadOnlyList<AnswerValidationError> Errors { get; } = errors;
}

public class ActivityExpectedResultsInvalidException(ActivityId id, string reason)
    : ActivityException(id, reason);

public class ActivityForbiddenException(ActivityId id, string message)
    : ActivityException(id, message);

public class ActivityUnknownException(ActivityId id, Exception innerException)
    : ActivityException(id, $"Unknown exception for the activity {id}", innerException);