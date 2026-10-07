using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Features.Claims.Commands;
using ClaimsModule.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ClaimsModule.Infrastructure.Storage;

/// <summary>
/// Documents live in the claim-documents container as {organisationId}/{claimId}/{file}. Downloads use read-only SAS
/// URLs with a short TTL, so document bytes never pass through the API (BR-D-02).
/// </summary>
public sealed class AzureBlobStorageService : IStorageService
{
    private readonly BlobContainerClient _container;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialised;

    public AzureBlobStorageService(IOptions<StorageOptions> options)
    {
        var azure = options.Value.AzureBlob;
        _container = new BlobServiceClient(azure.ConnectionString).GetBlobContainerClient(azure.ContainerName);
    }

    public async Task<string> UploadAsync(string relativePath, Stream content, string contentType, CancellationToken ct = default)
    {
        await EnsureContainerAsync(ct);
        var blob = _container.GetBlobClient(relativePath);
        await blob.UploadAsync(content, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);
        return $"{_container.Name}/{relativePath}";
    }

    public Task<Uri> GetDownloadUrlAsync(string storedPath, TimeSpan timeToLive, CancellationToken ct = default)
    {
        var prefix = _container.Name + "/";
        var blobName = storedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? storedPath[prefix.Length..] : storedPath;
        var blob = _container.GetBlobClient(blobName);

        if (!blob.CanGenerateSasUri)
        {
            throw new InvalidOperationException("The storage connection string must include an account key to issue SAS URLs.");
        }

        var sas = new BlobSasBuilder(BlobSasPermissions.Read, DateTimeOffset.UtcNow.Add(timeToLive))
        {
            BlobContainerName = _container.Name,
            BlobName = blobName,
            Resource = "b",
            // Saves as the clean file name instead of the internal "{prefix}-name" blob name.
            ContentDisposition = $"attachment; filename=\"{DocumentRules.DisplayName(blobName)}\"",
        };
        return Task.FromResult(blob.GenerateSasUri(sas));
    }

    private async Task EnsureContainerAsync(CancellationToken ct)
    {
        if (_initialised)
        {
            return;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (!_initialised)
            {
                await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
                _initialised = true;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }
}
