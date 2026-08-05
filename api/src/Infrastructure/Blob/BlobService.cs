using Application.Common.Interfaces;
using Azure.Storage.Blobs;
using LanguageExt;

namespace Infrastructure.Blob;

public class BlobService(BlobServiceClient blobServiceClient) : IBlobService
{
    public async Task<Either<Exception, Stream>> OpenWriteStream(
        string containerName,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(fileName);

            return await blobClient.OpenWriteAsync(
                overwrite: true,
                cancellationToken: cancellationToken);
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    public async Task<Either<Exception, Stream>> GetReadStream(
        string containerName,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(fileName);

            return await blobClient.OpenReadAsync(null, cancellationToken);
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    public async Task<Either<Exception, Unit>> RemoveByPrefix(
        string containerName,
        string prefix,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            var blobs = containerClient.GetBlobsAsync(prefix: $"{prefix}", cancellationToken: cancellationToken);
            await foreach (var blob in blobs)
            {
                await containerClient.DeleteBlobAsync(blobName: blob.Name, cancellationToken: cancellationToken);
            }

            return Unit.Default;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}