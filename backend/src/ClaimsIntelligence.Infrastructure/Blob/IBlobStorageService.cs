namespace ClaimsIntelligence.Infrastructure.Blob;

public interface IBlobStorageService
{
    Task EnsureContainerExistsAsync(string containerName, CancellationToken cancellationToken = default);

    Task UploadAsync(string containerName, string blobName, Stream content, string? contentType = null, CancellationToken cancellationToken = default);
    Task UploadAsync(string containerName, string blobName, byte[] content, string? contentType = null, CancellationToken cancellationToken = default);
    Task UploadTextAsync(string containerName, string blobName, string text, CancellationToken cancellationToken = default);

    Task<byte[]> DownloadAsync(string containerName, string blobName, CancellationToken cancellationToken = default);
    Task<string> DownloadTextAsync(string containerName, string blobName, CancellationToken cancellationToken = default);
    Task DownloadToFileAsync(string containerName, string blobName, string filePath, CancellationToken cancellationToken = default);

    Task DeleteBlobAsync(string containerName, string blobName, CancellationToken cancellationToken = default);
    Task<Dictionary<string, bool>> DeleteManyBlobsAsync(string containerName, IEnumerable<string> blobNames, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> ListBlobNamesAsync(string containerName, string? prefix = null, CancellationToken cancellationToken = default);
}
