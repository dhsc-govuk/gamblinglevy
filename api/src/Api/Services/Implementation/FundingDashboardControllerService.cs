using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Funding;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

public class FundingDashboardControllerService(
    IOrgFundingAllocationRepository allocationRepository,
    ISpendReportTotals spendReportTotals,
    IDateTimeProvider dateTimeProvider) : IFundingDashboardControllerService
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

        // The year in view is the financial year in progress — the same year the LA's open quarterly collections
        // are for, and therefore the year its spend is recorded against.
        //
        // Since BLUM-7608 the admin chooses the year an allocation is for, so the org can hold several rows;
        // AllocationSelector answers which of them describes the year in view (BLUM-6213 AC3). "Describes" is not
        // "is windowed to": an allocation row is created once at onboarding and never re-windowed, so an LA
        // onboarded in 2025/26 still holds only that row while reporting in 2026/27, and that row is still the
        // money it is spending.
        //
        // The year turning over is a real discontinuity and an accepted one: from 1 April an LA still filling in
        // its Q4 report for the year just ended sees the tile reset to the new year's £0 and its full allocation.
        // That is what the card says it is — the year in progress, named on it — and the Q4 figures stay correct
        // on the report screen, which is scoped to its own collection's year. Following the LA's open collection
        // instead would put the tile on a year the allocation may not be for, which is the thing being fixed here.
        //
        // The one case where the card leaves the year in progress is an organisation holding ONLY a later year —
        // onboarded in, say, February for the year starting in April. The selector falls forward to it and the
        // card is labelled with that year, so the tile never implies the money belongs to this year.
        var held = await allocationRepository.ListWithWindowsForOrganisationsAsync([organisationId], cancellationToken);
        var currentFinancialYear = FinancialYearRange.Current(dateTimeProvider.GetUtcNow());
        var (current, _) = AllocationSelector.SelectForFinancialYear(held, currentFinancialYear);

        var allocationYear = current?.Window.FinancialYear;
        var financialYear = allocationYear is not null && string.CompareOrdinal(allocationYear, currentFinancialYear) > 0
            ? allocationYear
            : currentFinancialYear;
        var allocatedAmount = current?.Allocation.AllocatedAmount ?? 0m;

        // Spend is bounded by the financial year on the LA's own collections, NOT by the year its allocation row
        // happens to be windowed to (BLUM-7701): binding it to the allocation's window left every organisation
        // still holding an earlier year's row summing a year it has no submissions in, so "Spent to date" never
        // moved however many reports it submitted.
        //
        // No quarter bound: unlike the quarter-bounded report screen, the tile is the org's true live cumulative
        // spend across every quarter recorded so far this year.
        var cumulativeSpend = await spendReportTotals.GetTotalForFinancialYearAsync(
            organisationId, financialYear, cancellationToken);
        var remainingBudget = allocatedAmount - cumulativeSpend;

        return new FundingDashboardDto(
            allocatedAmount,
            cumulativeSpend,
            remainingBudget,
            financialYear,
            current is not null);
    }
}