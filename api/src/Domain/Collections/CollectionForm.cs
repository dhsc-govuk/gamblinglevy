using Domain.Common;
using Domain.DataCollectionForms;

namespace Domain.Collections;

// Join row assigning a published form module to a collection (the collection's "form"). Modelled as an
// id-only reference satellite so it persists through the Collection aggregate without EF tracking conflicts.
public class CollectionForm : IEntity<CollectionId>
{
    public CollectionId Id => CollectionId;
    public CollectionId CollectionId { get; private set; }
    public FormModuleId FormModuleId { get; private set; }

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
}