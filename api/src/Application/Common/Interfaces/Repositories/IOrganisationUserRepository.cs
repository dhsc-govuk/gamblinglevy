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
    // Every active user of every listed organisation, in ONE query.
    //
    // GetActiveUsersAsync above is right for a single-organisation command, but the BLUM-7571 reminder sweep
    // resolves up to the whole ~153-organisation estate for a "submitted by all LAs" window, and calling it in a
    // loop is one round trip per organisation per sweep. Projected to a lean record rather than User so hundreds
    // of aggregates are never materialised just to read a mailbox.
    //
    // No role filter, matching GetActiveUsersAsync: both LA Admin and LA Lead are reminded, because the Lead is
    // usually the person who actually fills the report in.
    Task<IReadOnlyList<OrganisationUserContact>> GetActiveUsersForOrganisationsAsync(
        IReadOnlyCollection<OrganisationId> organisationIds,
        CancellationToken cancellationToken = default);

    Task<Option<OrganisationId>> FindOrganisationIdForUser(UserId userId, CancellationToken cancellationToken = default);
    Task<Option<OrganisationUser>> FindByUserIdAsync(UserId userId, CancellationToken cancellationToken = default);
    Task RemoveByOrganisationAsync(OrganisationId organisationId, CancellationToken cancellationToken = default);
    Task RemoveByUserIdAsync(UserId userId, CancellationToken cancellationToken = default);
}

public readonly record struct OrganisationUserContact(Guid OrganisationId, Guid UserId, string Email, string Name);