using Domain.Common;
using Domain.Users;

namespace Domain.Organisations;

public class OrgNeighbour : IEntity<OrgNeighbourId>
{
    public OrgNeighbourId Id { get; private set; }
    public OrganisationId OrganisationId { get; private set; }
    public OrganisationId NeighbourOrgId { get; private set; }
    public NeighbourSource Source { get; private set; }
    public string? ReferenceTool { get; private set; }
    public DateTime ImportedAt { get; private set; }
    public UserId ImportedBy { get; private set; }

    private OrgNeighbour(
        OrgNeighbourId id,
        OrganisationId organisationId,
        OrganisationId neighbourOrgId,
        NeighbourSource source,
        string? referenceTool,
        DateTime importedAt,
        UserId importedBy)
    {
        Id = id;
        OrganisationId = organisationId;
        NeighbourOrgId = neighbourOrgId;
        Source = source;
        ReferenceTool = referenceTool;
        ImportedAt = importedAt;
        ImportedBy = importedBy;
    }

    public static OrgNeighbour New(
        OrgNeighbourId id,
        OrganisationId organisationId,
        OrganisationId neighbourOrgId,
        NeighbourSource source,
        string? referenceTool,
        UserId importedBy)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(organisationId, nameof(organisationId));
        Guard.NotNull(neighbourOrgId, nameof(neighbourOrgId));
        Guard.NotNull(source, nameof(source));
        Guard.NotNull(importedBy, nameof(importedBy));

        return new OrgNeighbour(id, organisationId, neighbourOrgId, source, referenceTool, DateTime.UtcNow, importedBy);
    }
}