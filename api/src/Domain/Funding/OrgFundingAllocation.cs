using Domain.Common;
using Domain.Organisations;
using Domain.Users;

namespace Domain.Funding;

public class OrgFundingAllocation : IEntity<OrgFundingAllocationId>
{
    public OrgFundingAllocationId Id { get; private set; }
    public OrganisationId OrganisationId { get; private set; }
    public FundingWindowId FundingWindowId { get; private set; }
    public decimal AllocatedAmount { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public UserId CreatedBy { get; private set; }
    public DateTime? AmendedAt { get; private set; }
    public UserId? AmendedBy { get; private set; }

    private OrgFundingAllocation(
        OrgFundingAllocationId id,
        OrganisationId organisationId,
        FundingWindowId fundingWindowId,
        decimal allocatedAmount,
        DateTime createdAt,
        UserId createdBy,
        DateTime? amendedAt,
        UserId? amendedBy)
    {
        Id = id;
        OrganisationId = organisationId;
        FundingWindowId = fundingWindowId;
        AllocatedAmount = allocatedAmount;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        AmendedAt = amendedAt;
        AmendedBy = amendedBy;
    }

    public static OrgFundingAllocation New(
        OrgFundingAllocationId id,
        OrganisationId organisationId,
        FundingWindowId fundingWindowId,
        decimal allocatedAmount,
        UserId createdBy,
        DateTime utcNow)
    {
        Guard.NotNull(id, nameof(id));
        Guard.NotNull(organisationId, nameof(organisationId));
        Guard.NotNull(fundingWindowId, nameof(fundingWindowId));
        Guard.NotNull(createdBy, nameof(createdBy));
        Guard.GreaterThan(allocatedAmount, 0m, nameof(allocatedAmount));

        return new OrgFundingAllocation(id, organisationId, fundingWindowId, allocatedAmount, utcNow, createdBy, null, null);
    }

    public void UpdateAmount(decimal newAmount, UserId amendedBy, DateTime utcNow)
    {
        Guard.NotNull(amendedBy, nameof(amendedBy));
        Guard.GreaterThan(newAmount, 0m, nameof(newAmount));

        AllocatedAmount = newAmount;
        AmendedAt = utcNow;
        AmendedBy = amendedBy;
    }
}