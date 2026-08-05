using Api.Dtos;
using Api.Services.Abstract;
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
    IOrgFundingAllocationRepository allocationRepository) : IOrganisationsControllerService
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

        foreach (var org in organisations)
        {
            var allocation = await allocationRepository.FindLatestForOrganisationAsync(org.Id, cancellationToken);
            var allocationEntity = allocation.IsSome ? allocation.ValueUnsafe() : null;
            result.Add(LocalAuthorityDto.FromDomainModel(org, allocationEntity, null));
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
        var allocation = await allocationRepository.FindLatestForOrganisationAsync(org.Id, cancellationToken);
        var allocationEntity = allocation.IsSome ? allocation.ValueUnsafe() : null;

        return LocalAuthorityDto.FromDomainModel(org, allocationEntity, null);
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