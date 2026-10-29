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
using MediatR;

namespace Application.Organisations.Commands.CreateLocalAuthority;

public record CreateLocalAuthorityCommand : IRequest<Either<OrganisationException, CreateLocalAuthorityResult>>
{
    public string? Name { get; init; }
    public string? OnsCode { get; init; }
    public string? Region { get; init; }
    public string? Type { get; init; }
    public string? ECode { get; init; }
    public decimal AllocatedAmount { get; init; }

    // The financial year AllocatedAmount belongs to (BLUM-7608). Optional: an omitted value keeps the old
    // behaviour of stamping whatever FY is current by the clock, which is what the bulk-import path still does.
    public string? FinancialYear { get; init; }

    // The upcoming year's allocation, deliberately optional - DHSC may not know next year's figure at onboarding.
    // Its financial year is always FinancialYearRange.Next(FinancialYear), never chosen separately, so the two
    // rows cannot end up naming the same window or leaving a gap between them.
    public decimal? NextYearAllocatedAmount { get; init; }

    // The admin's answer to "When do you want to activate this account?" (BLUM-7945). Nullable only so that an
    // omitted answer reaches the validator, which rejects it, rather than binding to false and silently creating
    // the organisation deactivated.
    public bool? ActivateImmediately { get; init; }
}

public record CreateLocalAuthorityResult(
    Organisation Organisation,
    OrgFundingAllocation Allocation,
    FundingWindow Window,
    OrgFundingAllocation? NextYearAllocation = null,
    FundingWindow? NextYearWindow = null);

public class CreateLocalAuthorityCommandHandler(
    IOrganisationRepository organisationRepository,
    IOrgFundingAllocationRepository allocationRepository,
    IFundingWindowResolver fundingWindowResolver,
    IAuditLogRepository auditLogRepository,
    ITransactionRunner transactionRunner,
    PermissionsService permissionsService,
    IDateTimeProvider dateTimeProvider)
    : IRequestHandler<CreateLocalAuthorityCommand, Either<OrganisationException, CreateLocalAuthorityResult>>
{
    public async Task<Either<OrganisationException, CreateLocalAuthorityResult>> Handle(
        CreateLocalAuthorityCommand request,
        CancellationToken cancellationToken)
    {
        var actorResult = permissionsService.GetUserId();
        return await actorResult.MatchAsync<Either<OrganisationException, CreateLocalAuthorityResult>>(
            actorId => HandleWithActor(request, actorId, cancellationToken),
            e => new OrganisationUnknownException(OrganisationId.Empty(), e));
    }

    private async Task<Either<OrganisationException, CreateLocalAuthorityResult>> HandleWithActor(
        CreateLocalAuthorityCommand request,
        UserId actorId,
        CancellationToken cancellationToken)
    {
        var utcNow = dateTimeProvider.GetUtcNow();

        var name = request.Name!.Trim();
        // Null for a VCSE organisation, which has no ONS code; the validator guarantees a value for an LA.
        var onsCode = string.IsNullOrWhiteSpace(request.OnsCode) ? null : request.OnsCode!.Trim();

        var duplicateName = await organisationRepository.FindByNameAsync(name, cancellationToken);
        if (duplicateName.IsSome)
        {
            return duplicateName.Match<OrganisationException>(
                o => new OrganisationAlreadyExistsException(o.Id, name),
                () => new OrganisationUnknownException(OrganisationId.Empty(), new Exception("unreachable")));
        }

        if (onsCode is not null)
        {
            var duplicateOns = await organisationRepository.FindByOnsCodeAsync(onsCode, cancellationToken);
            if (duplicateOns.IsSome)
            {
                return duplicateOns.Match<OrganisationException>(
                    o => new OrganisationDuplicateOnsCodeException(o.Id, onsCode),
                    () => new OrganisationUnknownException(OrganisationId.Empty(), new Exception("unreachable")));
            }
        }

        // The organisation, its allocations and the audit row commit together (BLUM-8748).
        return await transactionRunner.RunAsync<OrganisationException, CreateLocalAuthorityResult>(
            async token =>
            {
                try
                {
                    var organisation = Organisation.New(
                        OrganisationId.New(),
                        name,
                        onsCode,
                        Region.From(request.Region!),
                        OrgType.From(request.Type!),
                        // Non-null by validator: CreateLocalAuthorityCommandValidator rejects an empty E-code before
                        // the handler runs, so there is no "no code supplied" case to fold into null here.
                        request.ECode!.Trim(),
                        // Non-null by validator, like the E-code above. "Activate it now" is invited - what every new
                        // organisation was before BLUM-7945, which its users can already sign in to and which can still be
                        // deleted (DeleteLocalAuthorityCommand refuses an active one). "Leave it deactivated" is suspended,
                        // the state deactivating an organisation leaves (BLUM-7667).
                        request.ActivateImmediately!.Value ? OnboardingStatus.Invited : OnboardingStatus.Suspended,
                        utcNow);

                    var savedOrg = await organisationRepository.AddAsync(organisation, token);

                    var financialYear = string.IsNullOrWhiteSpace(request.FinancialYear)
                        ? FinancialYearRange.Current(utcNow)
                        : request.FinancialYear!.Trim();

                    var window = await fundingWindowResolver.ResolveForFinancialYearAsync(financialYear, token);

                    var allocation = OrgFundingAllocation.New(
                        OrgFundingAllocationId.New(),
                        savedOrg.Id,
                        window.Id,
                        request.AllocatedAmount,
                        actorId,
                        utcNow);

                    var savedAllocation = await allocationRepository.AddAsync(allocation, token);

                    OrgFundingAllocation? savedNextYearAllocation = null;
                    FundingWindow? nextYearWindow = null;

                    if (request.NextYearAllocatedAmount.HasValue)
                    {
                        nextYearWindow = await fundingWindowResolver.ResolveForFinancialYearAsync(
                            FinancialYearRange.Next(financialYear),
                            token);

                        savedNextYearAllocation = await allocationRepository.AddAsync(
                            OrgFundingAllocation.New(
                                OrgFundingAllocationId.New(),
                                savedOrg.Id,
                                nextYearWindow.Id,
                                request.NextYearAllocatedAmount.Value,
                                actorId,
                                utcNow),
                            token);
                    }

                    await auditLogRepository.AppendAsync(
                        AuditLog.Create(
                            actorId,
                            AuditTargetType.Organisation,
                            savedOrg.Id.Value,
                            AuditAction.OrganisationCreated,
                            before: null,
                            after: OrganisationSnapshot.Serialize(
                                savedOrg,
                                savedAllocation,
                                window,
                                savedNextYearAllocation,
                                nextYearWindow),
                            organisationId: savedOrg.Id),
                        token);

                    return new CreateLocalAuthorityResult(savedOrg, savedAllocation, window, savedNextYearAllocation, nextYearWindow);
                }
                catch (Exception exception)
                {
                    return new OrganisationUnknownException(OrganisationId.Empty(), exception);
                }
            },
            cancellationToken);
    }
}