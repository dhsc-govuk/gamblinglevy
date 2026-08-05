using Domain.ValueObjects;
using LanguageExt;

namespace Application.Users.Interfaces;

public interface IUsersService
{
    Task<Option<string>> FindById(string userId, CancellationToken cancellationToken);
    Task<Option<string>> FindByEmail(string email, CancellationToken cancellationToken);

    Task<Either<Exception, string>> Add(
        Name userName,
        string email,
        bool isActive,
        CancellationToken cancellationToken);

    Task<Either<Exception, Unit>> Update(
        string id,
        Name userName,
        string email,
        CancellationToken cancellationToken);

    Task<Either<Exception, Unit>> Disable(string id, CancellationToken cancellationToken);

    Task<Either<Exception, Unit>> Enable(string id, CancellationToken cancellationToken);

    Task<Either<Exception, Unit>> HardDelete(string id, CancellationToken cancellationToken);

    Task<Either<Exception, Unit>> UpdateRole(string id, UserRole role, CancellationToken cancellationToken);
}