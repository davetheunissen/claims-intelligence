using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace ClaimsIntelligence.Infrastructure.Blob;

public class BlobStorageService(BlobServiceClient client, ILogger<BlobStorageService> logger) : IBlobStorageService
{
    private BlobContainerClient GetContainer(string containerName) => client.GetBlobContainerClient(containerName);

    public async Task EnsureContainerExistsAsync(string containerName, CancellationToken cancellationToken = default)
    {
        await GetContainer(containerName).CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
    }

    public async Task UploadAsync(string containerName, string blobName, Stream content, string? contentType = null, CancellationToken cancellationToken = default)
    {
        var blob = GetContainer(containerName).GetBlobClient(blobName);
        var options = contentType is not null ? new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } } : null;
        await blob.UploadAsync(content, options, cancellationToken);
    }

    public async Task UploadAsync(string containerName, string blobName, byte[] content, string? contentType = null, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream(content);
        await UploadAsync(containerName, blobName, stream, contentType, cancellationToken);
    }

    public async Task UploadTextAsync(string containerName, string blobName, string text, CancellationToken cancellationToken = default)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        await UploadAsync(containerName, blobName, bytes, "text/plain; charset=utf-8", cancellationToken);
    }

    public async Task<byte[]> DownloadAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        var blob = GetContainer(containerName).GetBlobClient(blobName);
        var response = await blob.DownloadContentAsync(cancellationToken);
        return response.Value.Content.ToArray();
    }

    public async Task<string> DownloadTextAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        var bytes = await DownloadAsync(containerName, blobName, cancellationToken);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    public async Task DownloadToFileAsync(string containerName, string blobName, string filePath, CancellationToken cancellationToken = default)
    {
        var blob = GetContainer(containerName).GetBlobClient(blobName);
        await blob.DownloadToAsync(filePath, cancellationToken);
    }

    public async Task DeleteBlobAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
    {
        var blob = GetContainer(containerName).GetBlobClient(blobName);
        await blob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    public async Task<Dictionary<string, bool>> DeleteManyBlobsAsync(string containerName, IEnumerable<string> blobNames, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, bool>();
        var container = GetContainer(containerName);

        foreach (var name in blobNames)
        {
            try
            {
                var response = await container.GetBlobClient(name).DeleteIfExistsAsync(cancellationToken: cancellationToken);
                results[name] = response.Value;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete blob {Container}/{Blob}", containerName, name);
                results[name] = false;
            }
        }

        return results;
    }

    public async IAsyncEnumerable<string> ListBlobNamesAsync(string containerName, string? prefix = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var container = GetContainer(containerName);
        await foreach (var item in container.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
        {
            yield return item.Name;
        }
    }
}
