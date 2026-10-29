using Domain.DataCollectionForms;

namespace Domain.Collections;

// BLUM-9103: one form of a collection moved from the version it recorded onto a newer one
// (Collection.FollowLatestFormVersions), kept so the caller can audit what moved.
public sealed record FormVersionMove(FormModuleId FormModuleId, FormModuleVersionId From, FormModuleVersionId To);