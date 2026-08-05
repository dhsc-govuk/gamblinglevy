using Api.Dtos;
using Application.Common.Permissions;
using LanguageExt;

namespace Api.Services.Abstract;

public interface IOrganisationUsersControllerService
{
    Task<IEnumerable<UserDto>> GetAll(
        Permission permission,
        Guid organisationId,
        CancellationToken cancellationToken);

    Task<Option<UserDetailDto>> Get(
        Permission permission,
        Guid organisationUserId,
        CancellationToken cancellationToken);
}