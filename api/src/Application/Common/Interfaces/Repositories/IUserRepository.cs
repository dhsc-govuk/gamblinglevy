using Application.Common.Permissions;
using Domain.Users;
using LanguageExt;

namespace Application.Common.Interfaces.Repositories;

public interface IUserRepository
{
    Task<Option<User>> FindById(UserId id, CancellationToken cancellationToken = default);

    // The user, if the permission may see them: DHSC admin sees everyone, an LA role only its own LA's users.
    Task<Option<User>> FindInScope(UserId id, Permission permission, CancellationToken cancellationToken = default);
    Task<Option<User>> FindByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<Option<User>> FindDuplicateAsync(string targetEmail, UserId sourceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> GetActiveAdminsAsync(CancellationToken cancellationToken = default);

    // Every user, as full entities rather than a projected DTO. Backs NormaliseUserEmailsCommand, which needs
    // SubId (to correct the matching Entra account) alongside Email — GetAllUsers's UserListItem carries neither.
    Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<User> AddAsync(User entity, CancellationToken cancellationToken = default);
    Task<User> UpdateAsync(User entity, CancellationToken cancellationToken = default);

    // saveChanges: false stages the update for the next SaveChanges on the shared context — UserDeactivator uses
    // it so a status change commits together with its audit row.
    Task<int> UpdateRangeAsync(IReadOnlyList<User> entities, CancellationToken cancellationToken = default, bool saveChanges = true);
    Task<User> RemoveAsync(User entity, CancellationToken cancellationToken = default);
}