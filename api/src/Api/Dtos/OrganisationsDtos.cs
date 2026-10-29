using Application.Common.Funding;
using Application.Common.Interfaces.Repositories;
using Domain.Funding;
using Domain.Organisations;

namespace Api.Dtos;

public record OrganisationDto(
    Guid Id,
    string Name,
    string? OnsCode,
    string Region,
    string Type,
    string ECode,
    string OnboardingStatus)
{
    public static OrganisationDto FromDomainModel(Organisation input)
        => new(
            input.Id.Value,
            input.Name,
            input.OnsCode,
            input.Region.ToString(),
            input.Type.ToString(),
            input.ECode,
            input.OnboardingStatus.ToString());
}

public record OrganisationHomeDto(
    string Name,
    int Admins,
    int Locations);

public record LocalAuthorityDto(
    Guid Id,
    string Name,
    string? OnsCode,
    string Region,
    string Type,
    string ECode,
    string OnboardingStatus,
    decimal? AllocatedAmount,
    string? FundingFinancialYear,
    decimal? NextYearAllocatedAmount,
    string? NextYearFinancialYear,
    DateTime CreatedAt,
    DateTime? UpdatedAt)
{
    public static LocalAuthorityDto FromDomainModel(
        Organisation org,
        OrgFundingAllocation? allocation,
        FundingWindow? window,
        OrgFundingAllocation? nextYearAllocation = null,
        FundingWindow? nextYearWindow = null)
        => new(
            org.Id.Value,
            org.Name,
            org.OnsCode,
            org.Region.ToString(),
            org.Type.ToString(),
            org.ECode,
            org.OnboardingStatus.ToString(),
            allocation?.AllocatedAmount,
            window?.FinancialYear,
            nextYearAllocation?.AllocatedAmount,
            nextYearWindow?.FinancialYear,
            org.CreatedAt,
            org.UpdatedAt);

    // The read path: an organisation holds one allocation per financial year, and AllocationSelector decides
    // which two of them the admin screens show.
    public static LocalAuthorityDto FromAllocations(
        Organisation org,
        IEnumerable<OrgAllocationWithWindow> allocations,
        DateTime nowUtc)
    {
        var (current, next) = AllocationSelector.Select(allocations, nowUtc);
        return FromDomainModel(org, current?.Allocation, current?.Window, next?.Allocation, next?.Window);
    }
}

// ECode stays nullable on the request records even though it is required: model binding cannot enforce a
// non-nullable reference type, so an omitted field would arrive as null regardless. Leaving it nullable keeps
// the validator the single gate that answers 400 with "Enter the E-code" instead of a binder-shaped message.
public record CreateLocalAuthorityRequestDto(
    string Name,
    string? OnsCode,
    string Region,
    string Type,
    string? ECode,
    decimal AllocatedAmount,
    string? FinancialYear = null,
    decimal? NextYearAllocatedAmount = null,
    bool? ActivateImmediately = null);

public record UpdateLocalAuthorityRequestDto(
    string Name,
    string? OnsCode,
    string Region,
    string Type,
    string? ECode,
    string? OnboardingStatus,
    bool ConfirmOnsCodeChange);

public record AmendLocalAuthorityAllocationRequestDto(
    decimal AllocatedAmount,
    string? FinancialYear = null,
    decimal? NextYearAllocatedAmount = null);

// The admin-user members default so the bulk-allocations action, which shares this DTO and has no
// admin-user concept, keeps mapping with five arguments and keeps sending nulls for them.
public record BulkImportRowOutcomeDto(
    int RowNumber,
    string? OnsCode,
    string? Name,
    string Outcome,
    string? Reason,
    string? AdminUserOutcome = null,
    string? AdminUserEmail = null,
    string? AdminUserReason = null);

public record BulkImportResultDto(
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<BulkImportRowOutcomeDto> Rows,
    int AdminUsersCreatedCount = 0,
    int AdminUsersFailedCount = 0,
    int AdminUsersNotAttemptedCount = 0);