using Api.Dtos;
using Api.Services.Abstract;
using Application.Common.Interfaces.Queries;
using Application.Common.Permissions;
using Domain.Organisations;
using Domain.OrganisationUsers;
using LanguageExt;

namespace Api.Services.Implementation;

public class OrganisationUsersControllerService(
    IOrganisationUserQueries organisationUserQueries,
    IUserQueries userQueries) : IOrganisationUsersControllerService
{
    public async Task<IEnumerable<UserDto>> GetAll(
        Permission permission,
        Guid organisationId,
        CancellationToken cancellationToken)
    {
        var users = await organisationUserQueries.GetUsers(
            new OrganisationId(organisationId),
            permission,
            cancellationToken);

        return users.Select(UserDto.FromDomainModel);
    }

    public async Task<Option<UserDetailDto>> Get(
        Permission permission,
        Guid organisationUserId,
        CancellationToken cancellationToken)
    {
        var orgUser = await organisationUserQueries.GetOrganisationUserById(
            new OrganisationUserId(organisationUserId),
            permission,
            cancellationToken);

        return await orgUser.MatchAsync(
            async u =>
            {
                var detail = await userQueries.FindByIdWithDetails(u.UserId, cancellationToken);
                return detail.Match(
                    item => (Option<UserDetailDto>)UserDetailDto.FromDetailItem(item),
                    () => Option<UserDetailDto>.None);
            },
            () => Task.FromResult(Option<UserDetailDto>.None));
    }
}