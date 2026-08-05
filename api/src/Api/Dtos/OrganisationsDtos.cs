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
    DateTime CreatedAt,
    DateTime? UpdatedAt)
{
    public static LocalAuthorityDto FromDomainModel(Organisation org, OrgFundingAllocation? allocation, FundingWindow? window)
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
            org.CreatedAt,
            org.UpdatedAt);
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
    decimal AllocatedAmount);

public record UpdateLocalAuthorityRequestDto(
    string Name,
    string? OnsCode,
    string Region,
    string Type,
    string? ECode,
    string? OnboardingStatus,
    bool ConfirmOnsCodeChange);

public record AmendLocalAuthorityAllocationRequestDto(decimal AllocatedAmount);

public record BulkImportRowOutcomeDto(
    int RowNumber,
    string? OnsCode,
    string? Name,
    string Outcome,
    string? Reason);

public record BulkImportResultDto(
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<BulkImportRowOutcomeDto> Rows);