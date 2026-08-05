using Application.Common.Interfaces;
using LanguageExt;

namespace Tests.Common.Services;

public class InMemoryBlobService : IBlobService
{
    public Task<Either<Exception, Stream>> OpenWriteStream(string containerName, string fileName, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Either<Exception, Stream>>(new MemoryStream());
    }

    public Task<Either<Exception, Stream>> GetReadStream(string containerName, string fileName, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<Either<Exception, Unit>> RemoveByPrefix(string containerName, string prefix, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}