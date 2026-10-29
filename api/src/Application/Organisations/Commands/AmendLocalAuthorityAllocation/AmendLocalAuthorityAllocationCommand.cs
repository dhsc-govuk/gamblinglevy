using Application.Common.Funding;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Application.Organisations.Audit;
using Application.Organisations.Exceptions;
using Domain.AuditLogs;
using Domain.Funding;
using Domain.Organisations;
using Domain.Users;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using MediatR;

namespace Application.Organisations.Commands.AmendLocalAuthorityAllocation;

public record AmendLocalAuthorityAllocationCommand : IRequest<Either<OrganisationException, AmendLocalAuthorityAllocationResult>>
{
    public Guid Id { get; init; }
    public decimal AllocatedAmount { get; init; }

    // The financial year AllocatedAmount applies to (BLUM-7608). Optional, and an omitted value keeps the
    // pre-BLUM-7608 behaviour exactly: amend whichever year the organisation's latest allocation already names.
    public string? FinancialYear { get; init; }

    // The upcoming year's allocation. Its year is always FinancialYearRange.Next of the resolved year above,
    // never chosen separately, so the two rows cannot name the same window or leave a gap.
    public decimal? NextYearAllocatedAmount { get; init; }
}

public record AmendLocalAuthorityAllocationResult(
    Organisation Organisation,
    OrgFundingAllocation Allocation,
    FundingWindow Window,
    OrgFundingAllocation? NextYearAllocation = null,
    FundingWindow? NextYearWindow = null);

public class AmendLocalAuthorityAllocationCommandHandler(
    IOrganisationRepository organisationRepository,
    IOrgFundingAllocationRepository allocationRepository,
    IFundingWindowRepository fundingWindowRepository,
    IFundingWindowResolver fundingWindowResolver,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<AmendLocalAuthorityAllocationCommand, Either<OrganisationException, AmendLocalAuthorityAllocationResult>>
{
    public async Task<Either<OrganisationException, AmendLocalAuthorityAllocationResult>> Handle(
        AmendLocalAuthorityAllocationCommand request,
        CancellationToken cancellationToken)
    {
        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<OrganisationException, AmendLocalAuthorityAllocationResult>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new OrganisationUnknownException(new OrganisationId(request.Id), e));
    }

    private async Task<Either<OrganisationException, AmendLocalAuthorityAllocationResult>> HandleWithActor(
        AmendLocalAuthorityAllocationCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var organisationId = new OrganisationId(request.Id);
        var lookup = await organisationRepository.GetOrganisationById(organisationId, cancellationToken);
        if (lookup.IsNone)
        {
            return new OrganisationDoesNotExistException(organisationId);
        }

        var organisation = lookup.ValueUnsafe();

        // Everything the organisation holds, so the year in progress can be told apart from the upcoming one.
        // Deliberately NOT FindLatestForOrganisationAsync: that orders by CreatedAt, and since create and amend
        // can now write two rows in one request, "latest" means the UPCOMING year - so the audit "before" would
        // name the wrong year and an amend with no year supplied would silently change next year's money.
        var held = await allocationRepository.ListWithWindowsForOrganisationsAsync([organisation.Id], cancellationToken);
        var (existing, existingNext) = AllocationSelector.Select(held, dateTimeProvider.GetUtcNow());

        var financialYear = ResolveTargetFinancialYear(request, existing);
        if (financialYear is null)
        {
            // No year asked for and nothing to infer one from. Preserved as a 404 rather than quietly inventing
            // the current year, because "amend" against an organisation that has never been allocated anything
            // is a client error, not a create.
            return new OrganisationAllocationNotFoundException(organisation.Id);
        }

        var before = OrganisationSnapshot.Serialize(
            organisation,
            existing?.Allocation,
            existing?.Window,
            existingNext?.Allocation,
            existingNext?.Window);

        // The allocations and the audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<OrganisationException, AmendLocalAuthorityAllocationResult>(
            async token =>
            {
                try
                {
                    var (window, allocation) = await UpsertAsync(organisation.Id, financialYear, request.AllocatedAmount, actorId, token);

                    FundingWindow? nextYearWindow = null;
                    OrgFundingAllocation? nextYearAllocation = null;

                    if (request.NextYearAllocatedAmount.HasValue)
                    {
                        (nextYearWindow, nextYearAllocation) = await UpsertAsync(
                            organisation.Id,
                            FinancialYearRange.Next(financialYear),
                            request.NextYearAllocatedAmount.Value,
                            actorId,
                            token);
                    }
                    else
                    {
                        // Omitting the amount means "leave the upcoming year alone", which is what the amend screen's
                        // hint promises. Read the untouched row back so the response and the audit "after" describe what
                        // is actually stored - reporting null here would read as a removal that never happened.
                        var untouched = await FindForFinancialYearAsync(
                            organisation.Id,
                            FinancialYearRange.Next(financialYear),
                            token);
                        (nextYearWindow, nextYearAllocation) = (untouched?.Window, untouched?.Allocation);
                    }

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Organisation,
                            organisation.Id.Value,
                            AuditAction.OrganisationAllocationAmended,
                            before,
                            OrganisationSnapshot.Serialize(organisation, allocation, window, nextYearAllocation, nextYearWindow),
                            organisationId: organisation.Id),
                        token);

                    return new AmendLocalAuthorityAllocationResult(organisation, allocation, window, nextYearAllocation, nextYearWindow);
                }
                catch (Exception exception)
                {
                    return new OrganisationUnknownException(organisation.Id, exception);
                }
            },
            cancellationToken);
    }

    // Upsert rather than update-in-place, and deliberately NOT "move the existing row onto the new year": an
    // allocation already recorded against 2025/26 is what every submission frozen in 2025/26 was measured
    // against, so re-labelling it would rewrite history. Setting a year the organisation has no row for adds
    // one; setting a year it does have updates that row.
    private async Task<(FundingWindow Window, OrgFundingAllocation Allocation)> UpsertAsync(
        OrganisationId organisationId,
        string financialYear,
        decimal amount,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var window = await fundingWindowResolver.ResolveForFinancialYearAsync(financialYear, cancellationToken);
        var existing = await allocationRepository.FindForOrganisationAndWindowAsync(organisationId, window.Id, cancellationToken);

        if (existing.IsSome)
        {
            var allocation = existing.ValueUnsafe();
            allocation.UpdateAmount(amount, actorId, dateTimeProvider.GetUtcNow());
            return (window, await allocationRepository.UpdateAsync(allocation, cancellationToken));
        }

        var created = OrgFundingAllocation.New(OrgFundingAllocationId.New(), organisationId, window.Id, amount, actorId, dateTimeProvider.GetUtcNow());
        return (window, await allocationRepository.AddAsync(created, cancellationToken));
    }

    private async Task<OrgAllocationWithWindow?> FindForFinancialYearAsync(
        OrganisationId organisationId,
        string financialYear,
        CancellationToken cancellationToken)
    {
        // FindByFinancialYearAsync, not the resolver: the resolver PERSISTS a window when it finds none, and
        // this is a read taken purely to describe what already exists.
        var window = await fundingWindowRepository.FindByFinancialYearAsync(financialYear, cancellationToken);
        if (window.IsNone)
        {
            return null;
        }

        var fundingWindow = window.ValueUnsafe();
        var allocation = await allocationRepository.FindForOrganisationAndWindowAsync(
            organisationId,
            fundingWindow.Id,
            cancellationToken);

        return allocation.IsSome ? new OrgAllocationWithWindow(allocation.ValueUnsafe(), fundingWindow) : null;
    }

    private static string? ResolveTargetFinancialYear(
        AmendLocalAuthorityAllocationCommand request,
        OrgAllocationWithWindow? existing)
    {
        if (!string.IsNullOrWhiteSpace(request.FinancialYear))
        {
            return request.FinancialYear!.Trim();
        }

        return existing?.Window.FinancialYear;
    }
}