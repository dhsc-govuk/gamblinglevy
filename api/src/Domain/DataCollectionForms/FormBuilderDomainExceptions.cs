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

public class FormModuleNotPublishedException(FormModuleId id)
    : Exception($"Form module {id} is not published, so there is no published form to start a draft from.");

// BLUM-9102: thrown by IFormModuleVersionRepository when the (module, version number) unique index refuses a version,
// i.e. a concurrent publish of the same module took the number first.
public class FormModuleVersionNumberTakenException(FormModuleId id, int versionNumber)
    : Exception($"Version {versionNumber} of form module {id} already exists.");