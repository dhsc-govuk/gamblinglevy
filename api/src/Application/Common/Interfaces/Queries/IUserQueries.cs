using Domain.Users;
using LanguageExt;

namespace Application.Common.Interfaces.Queries;

public record UserListItem(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Status,
    string Role,
    string? OrganisationName);

public record UserHistoryEntry(string ActorName, DateTime OccurredAt);

public record UserDetailItem(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Status,
    string Role,
    string? OrganisationName,
    Guid? OrganisationId,
    bool MfaEnabled,
    UserHistoryEntry? CreatedHistory,
    UserHistoryEntry? LastUpdatedHistory,
    UserHistoryEntry? DeletedHistory);

public interface IUserQueries
{
    Task<int> Count(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<User>> GetAdminUsers(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserListItem>> GetAllUsers(CancellationToken cancellationToken = default);
    Task<Option<User>> FindById(UserId id, CancellationToken cancellationToken = default);
    Task<Option<UserDetailItem>> FindByIdWithDetails(UserId id, CancellationToken cancellationToken = default);
}