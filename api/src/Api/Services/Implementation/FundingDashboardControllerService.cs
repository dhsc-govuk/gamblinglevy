using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Funding;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

public class FundingDashboardControllerService(
    IFundingWindowResolver fundingWindowResolver,
    IOrgFundingAllocationRepository allocationRepository,
    ISpendReportTotals spendReportTotals) : IFundingDashboardControllerService
{
    public async Task<FundingDashboardDto> GetDashboard(Permission permission, CancellationToken cancellationToken)
    {
        // IsNone/ValueUnsafe, not Match — Option.Match throws on a null result (see
        // ActivitiesControllerService.ResolveOrg), which would defeat the null guard immediately below.
        var organisationId = permission.OrganisationId.IsNone ? null : permission.OrganisationId.ValueUnsafe();
        if (organisationId is null)
        {
            return new FundingDashboardDto(0m, 0m, 0m, null, false);
        }

        // Source of truth is the allocation for the current funding window (BLUM-6213 AC3).
        var window = await fundingWindowResolver.ResolveCurrentAsync(cancellationToken);
        var allocation = await allocationRepository.FindForOrganisationAndWindowAsync(
            organisationId, window.Id, cancellationToken);

        var allocatedAmount = allocation.Match(a => a.AllocatedAmount, () => 0m);
        var cumulativeSpend = await spendReportTotals.GetApprovedTotalForWindowAsync(
            organisationId, window.Id, cancellationToken);
        var remainingBudget = allocatedAmount - cumulativeSpend;

        return new FundingDashboardDto(
            allocatedAmount,
            cumulativeSpend,
            remainingBudget,
            window.FinancialYear,
            allocation.IsSome);
    }
}