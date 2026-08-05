using Application.Users.Interfaces;
using Domain.ValueObjects;
using LanguageExt;

namespace Tests.Common.Services;

public class InMemoryUsersService : IUsersService
{
    public Task<Option<string>> FindById(string userId, CancellationToken cancellationToken)
    {
        return Task.FromResult<Option<string>>(Guid.NewGuid().ToString());
    }

    public Task<Option<string>> FindByEmail(string email, CancellationToken cancellationToken)
    {
        return Task.FromResult<Option<string>>(Guid.NewGuid().ToString());
    }

    public Task<Either<Exception, string>> Add(Name userName, string email, bool isActive, CancellationToken cancellationToken)
    {
        return Task.FromResult<Either<Exception, string>>(Guid.NewGuid().ToString());
    }

    public Task<Either<Exception, Unit>> Update(string id, Name userName, string email, CancellationToken cancellationToken)
    {
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public Task<Either<Exception, Unit>> Disable(string id, CancellationToken cancellationToken)
    {
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public Task<Either<Exception, Unit>> Enable(string id, CancellationToken cancellationToken)
    {
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public Task<Either<Exception, Unit>> HardDelete(string id, CancellationToken cancellationToken)
    {
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public Task<Either<Exception, Unit>> UpdateRole(string id, UserRole role, CancellationToken cancellationToken)
    {
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}