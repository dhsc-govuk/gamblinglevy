namespace Api.Dtos;

public record FundingDashboardDto(
    decimal AllocatedAmount,
    decimal CumulativeSpend,
    decimal RemainingBudget,
    string? FinancialYear,
    bool HasAllocation);