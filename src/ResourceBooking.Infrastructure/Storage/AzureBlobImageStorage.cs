using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;
using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Infrastructure.Storage;

public class AzureBlobImageStorage : IImageStorage
{
    private readonly BlobContainerClient container;

    public AzureBlobImageStorage(IOptions<ImageStorageOptions> options)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.AzureBlobConnectionString))
            throw new InvalidOperationException("ImageStorage:AzureBlobConnectionString is required for Azure image storage.");
        if (string.IsNullOrWhiteSpace(settings.AzureBlobContainerName))
            throw new InvalidOperationException("ImageStorage:AzureBlobContainerName is required for Azure image storage.");

        container = new BlobContainerClient(settings.AzureBlobConnectionString, settings.AzureBlobContainerName);
    }

    private BlobClient Resolve(string key)
    {
        // Keep the same server-generated relative keys as local storage.
        if (string.IsNullOrWhiteSpace(key) || Path.IsPathRooted(key) || key.Contains('\\') ||
            key.Split('/').Any(part => part is "" or "." or ".." || part.Contains(':')))
            throw new InvalidOperationException("Invalid image storage key.");

        return container.GetBlobClient(key);
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var blob = Resolve(key);
        // New containers are private; images continue to be served through the API.
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        // Match FileMode.CreateNew: never replace an existing image.
        await blob.UploadAsync(content, overwrite: false, cancellationToken: cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var blob = Resolve(key);
        try
        {
            return await blob.OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.BlobNotFound.ToString() ||
                                               ex.ErrorCode == BlobErrorCode.ContainerNotFound.ToString())
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var blob = Resolve(key);
        await blob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
    }
}
