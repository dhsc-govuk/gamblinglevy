using LanguageExt;

namespace Application.Common.Interfaces;

public interface IBlobService
{
    Task<Either<Exception, Stream>> OpenWriteStream(
        string containerName,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<Either<Exception, Stream>> GetReadStream(
        string containerName,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<Either<Exception, Unit>> RemoveByPrefix(
        string containerName,
        string prefix,
        CancellationToken cancellationToken = default);
}