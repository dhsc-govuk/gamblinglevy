using System.Text.Json;
using Domain.Funding;
using Domain.Organisations;

namespace Application.Organisations.Audit;

internal static class OrganisationSnapshot
{
    // The window arguments name the financial year each allocation belongs to (BLUM-7608). Without them the
    // audit diff records two bare amounts and the reader cannot tell which year moved - the exact ambiguity
    // this ticket removed from the UI.
    public static string Serialize(
        Organisation organisation,
        OrgFundingAllocation? allocation = null,
        FundingWindow? window = null,
        OrgFundingAllocation? nextYearAllocation = null,
        FundingWindow? nextYearWindow = null)
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
            financialYear = window?.FinancialYear,
            nextYearAllocatedAmount = nextYearAllocation?.AllocatedAmount,
            nextYearFundingWindowId = nextYearAllocation?.FundingWindowId.Value,
            nextYearFinancialYear = nextYearWindow?.FinancialYear,
        });
    }
}