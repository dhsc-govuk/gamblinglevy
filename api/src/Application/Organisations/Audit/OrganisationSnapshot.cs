using System.Text.Json;
using Domain.Funding;
using Domain.Organisations;

namespace Application.Organisations.Audit;

internal static class OrganisationSnapshot
{
    public static string Serialize(Organisation organisation, OrgFundingAllocation? allocation = null)
    {
        return JsonSerializer.Serialize(new
        {
            id = organisation.Id.Value,
            name = organisation.Name,
            onsCode = organisation.OnsCode,
            region = organisation.Region.ToString(),
            type = organisation.Type.ToString(),
            eCode = organisation.ECode,
            onboardingStatus = organisation.OnboardingStatus.ToString(),
            allocatedAmount = allocation?.AllocatedAmount,
            fundingWindowId = allocation?.FundingWindowId.Value,
        });
    }
}