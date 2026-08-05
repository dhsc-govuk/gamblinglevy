namespace Domain.DataCollectionForms;

// Defensive domain guards: thrown when a mutating operation violates a forms-builder module invariant.
// Application handlers pre-check and return a typed exception, so these act as a last line of defence rather
// than the primary control-flow signal.
public class FormModulePublishedException(FormModuleId id)
    : Exception($"Form module {id} is published and its structure can no longer be modified.");

public class FormModuleEmptyOnPublishException(FormModuleId id)
    : Exception($"Form module {id} cannot be published with no questions.");

public class FormModuleAlreadyPublishedException(FormModuleId id)
    : Exception($"Form module {id} is already published.");