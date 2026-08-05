using Application.Common.Permissions;
using Domain.Organisations;
using Domain.OrganisationUsers;
using Domain.Users;
using LanguageExt;

namespace Application.Common.Interfaces.Repositories;

public interface IOrganisationUserRepository
{
    Task<Option<OrganisationUser>> GetOrganisationUserById(OrganisationUserId id, Permission permission, CancellationToken cancellationToken = default);
    Task<OrganisationUser> AddAsync(OrganisationUser entity, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> GetActiveOrgAdminsAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> GetActiveUsersAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);
    Task<Option<OrganisationId>> FindOrganisationIdForUser(UserId userId, CancellationToken cancellationToken = default);
    Task<Option<OrganisationUser>> FindByUserIdAsync(UserId userId, CancellationToken cancellationToken = default);
    Task<OrganisationUser> RemoveAsync(OrganisationUser entity, CancellationToken cancellationToken = default);
    Task RemoveByOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);
    Task RemoveByUserIdAsync(UserId userId, CancellationToken cancellationToken = default);
}