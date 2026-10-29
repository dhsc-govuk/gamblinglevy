using Domain.Common;
using Domain.Users;

namespace Domain.DataCollectionForms;

// BLUM-9102: a fixed copy of a form module as it stood when it was published. Each publish that changes the
// module's content adds the next version; nothing ever changes one. Definition is the whole module — sections,
// questions and options — as JSON (FormModuleDefinition in Application builds it), and ContentHash is the hash of
// that content without ids, which is how a publish with nothing changed is told apart from a real one.
//
// Immutability is enforced three times: this class has no mutators, IFormModuleVersionRepository can only add, and
// a trigger on form_module_versions refuses UPDATE and DELETE (migration BLUM_9102_FormModuleVersions).
public class FormModuleVersion : IEntity<FormModuleVersionId>
{
    public FormModuleVersionId Id { get; private set; }
    public FormModuleId FormModuleId { get; private set; }
    public int VersionNumber { get; private set; }
    public string Definition { get; private set; }
    public string ContentHash { get; private set; }
    public DateTime PublishedAt { get; private set; }
    public UserId PublishedBy { get; private set; }

    private FormModuleVersion(
        FormModuleVersionId id,
        FormModuleId formModuleId,
        int versionNumber,
        string definition,
        string contentHash,
        DateTime publishedAt,
        UserId publishedBy)
    {
        Id = id;
        FormModuleId = formModuleId;
        VersionNumber = versionNumber;
        Definition = definition;
        ContentHash = contentHash;
        PublishedAt = publishedAt;
        PublishedBy = publishedBy;
    }

    public static FormModuleVersion Create(
        FormModuleVersionId id,
        FormModuleId formModuleId,
        int versionNumber,
        string definition,
        string contentHash,
        UserId publishedBy,
        DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(formModuleId, nameof(formModuleId));
        Guard.GreaterThan(versionNumber, 0, nameof(versionNumber));
        Guard.NotNullOrEmptyOrWhiteSpace(definition, nameof(definition));
        Guard.NotNullOrEmptyOrWhiteSpace(contentHash, nameof(contentHash));
        Guard.NotNull(publishedBy, nameof(publishedBy));

        return new FormModuleVersion(id, formModuleId, versionNumber, definition, contentHash, utcNow, publishedBy);
    }
}