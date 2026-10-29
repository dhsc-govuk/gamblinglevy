using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Interfaces;
using Application.Common.Interfaces.Queries;
using Application.Common.Interfaces.Repositories;
using Application.Common.Permissions;
using Domain.Organisations;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;

namespace Api.Services.Implementation;

public class OrganisationsControllerService(
    IOrganisationQueries organisationQueries,
    IOrganisationUserQueries organisationUserQueries,
    ILocationQueries locationQueries,
    IOrgFundingAllocationRepository allocationRepository,
    IDateTimeProvider dateTimeProvider) : IOrganisationsControllerService
{
    public async Task<IEnumerable<OrganisationDto>> GetAll(CancellationToken cancellationToken)
    {
        var organisations = await organisationQueries.GetOrganisations(cancellationToken);
        return organisations.Select(OrganisationDto.FromDomainModel);
    }

    public async Task<Option<OrganisationDto>> Get(Guid organisationId, CancellationToken cancellationToken)
    {
        var entityId = new OrganisationId(organisationId);
        var organisation = await organisationQueries.GetOrganisationById(entityId, cancellationToken);
        return organisation.Match(
            o => (Option<OrganisationDto>)OrganisationDto.FromDomainModel(o),
            () => Option<OrganisationDto>.None);
    }

    public async Task<IEnumerable<LocalAuthorityDto>> GetLocalAuthorities(string? onboardingStatus, CancellationToken cancellationToken)
    {
        var organisations = await organisationQueries.GetLocalAuthorities(onboardingStatus, cancellationToken);
        var result = new List<LocalAuthorityDto>(organisations.Count);

        // One query for every organisation's allocations rather than one per organisation inside the loop: an
        // organisation now holds a row per financial year and the list has to show the year alongside each
        // amount, so the old FindLatestForOrganisationAsync-per-row shape would have tripled an N+1.
        var allocations = await allocationRepository.ListWithWindowsForOrganisationsAsync(
            organisations.Select(o => o.Id).ToList(),
            cancellationToken);

        var byOrganisation = allocations
            .GroupBy(x => x.Allocation.OrganisationId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<OrgAllocationWithWindow>)[.. g]);

        var nowUtc = dateTimeProvider.GetUtcNow();

        foreach (var org in organisations)
        {
            var forOrg = byOrganisation.TryGetValue(org.Id, out var rows) ? rows : [];
            result.Add(LocalAuthorityDto.FromAllocations(org, forOrg, nowUtc));
        }

        return result;
    }

    public async Task<Option<LocalAuthorityDto>> GetLocalAuthority(Guid organisationId, CancellationToken cancellationToken)
    {
        var entityId = new OrganisationId(organisationId);
        var organisation = await organisationQueries.GetOrganisationById(entityId, cancellationToken);

        if (organisation.IsNone)
        {
            return Option<LocalAuthorityDto>.None;
        }

        var org = organisation.ValueUnsafe();
        var allocations = await allocationRepository.ListWithWindowsForOrganisationsAsync([org.Id], cancellationToken);

        return LocalAuthorityDto.FromAllocations(org, allocations, dateTimeProvider.GetUtcNow());
    }

    public async Task<Option<OrganisationHomeDto>> Totals(
        Permission permission,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        var entityId = new OrganisationId(organisationId);

        return await organisationQueries.GetOrganisationById(entityId, cancellationToken)
            .MatchAsync<Organisation, Option<OrganisationHomeDto>>(
                async o => new OrganisationHomeDto(
                    Name: o.Name,
                    Admins: await organisationUserQueries.Count(entityId, permission, cancellationToken),
                    Locations: await locationQueries.Count(entityId, permission, cancellationToken)),
                () => Option<OrganisationHomeDto>.None);
    }
}