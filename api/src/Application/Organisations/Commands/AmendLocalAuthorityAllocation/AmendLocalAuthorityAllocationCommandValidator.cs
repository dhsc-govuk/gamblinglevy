using Application.Common.Validation;
using FluentValidation;

namespace Application.Organisations.Commands.AmendLocalAuthorityAllocation;

public class AmendLocalAuthorityAllocationCommandValidator : AbstractValidator<AmendLocalAuthorityAllocationCommand>
{
    public AmendLocalAuthorityAllocationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        // Was GreaterThan(0) only. org_funding_allocations.allocated_amount is numeric(18,2), so a value
        // above sixteen integer digits raised 22003 as a 500, and anything with more than two decimal places
        // was silently rounded on the way in (BLUM-7240).
        RuleFor(x => x.AllocatedAmount).PositiveMoney("Annual Allocation");

        // Optional - omitted means "the year this organisation's allocation already names" - but checked when
        // supplied, because the year selects the funding window by exact string equality (BLUM-7608).
        RuleFor(x => x.FinancialYear)
            .FinancialYearFormat()
            .When(x => !string.IsNullOrWhiteSpace(x.FinancialYear));
        RuleFor(x => x.NextYearAllocatedAmount).PositiveMoney("Annual Allocation");
    }
}