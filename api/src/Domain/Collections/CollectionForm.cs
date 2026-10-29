using Domain.Common;
using Domain.DataCollectionForms;

namespace Domain.Collections;

// Join row assigning a published form module to a collection (the collection's "form"). Modelled as an
// id-only reference satellite so it persists through the Collection aggregate without EF tracking conflicts.
// For a maturity assessment the id is a catalogue section's fixed id (MaturityCollectionForms, BLUM-7943) rather
// than a form_modules row — the column deliberately has no foreign key.
//
// FormModuleVersionId (BLUM-9103) is the version of the form this collection uses, recorded when the collection is
// published and moved onto each newer version until its window opens (Collection.FollowsLatestFormVersions). It stays
// null for a collection never published and for a maturity catalogue section, which has no versions.
// IsVersionChosen is left over from the per-collection version choice, which the PO rejected. Nothing reads it; the
// column stays until BLUM_9087 is retired (migrations are expand-only), and recording a version writes it false.
public class CollectionForm : IEntity<CollectionId>
{
    public CollectionId Id => CollectionId;
    public CollectionId CollectionId { get; private set; }
    public FormModuleId FormModuleId { get; private set; }
    public FormModuleVersionId? FormModuleVersionId { get; private set; }
    public bool IsVersionChosen { get; private set; }

    private CollectionForm(
        CollectionId collectionId,
        FormModuleId formModuleId)
    {
        CollectionId = collectionId;
        FormModuleId = formModuleId;
    }

    public static CollectionForm Create(
        CollectionId collectionId,
        FormModuleId formModuleId)
    {
        Guard.NotNull(collectionId, nameof(collectionId));
        Guard.NotNull(formModuleId, nameof(formModuleId));

        return new CollectionForm(collectionId, formModuleId);
    }

    internal void PinVersion(FormModuleVersionId versionId)
    {
        Guard.NotNull(versionId, nameof(versionId));
        FormModuleVersionId = versionId;
        IsVersionChosen = false;
    }
}