using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nichotan.Application.Common;

namespace Nichotan.Infrastructure.Storage;

/// <summary>
/// Private document storage on Azure Blob Storage. The container is created without public access and is only reachable through
/// the API or a short-lived SAS link. In Azure the app signs in with its managed identity (no keys to store or rotate);
/// a connection string is supported for local development against Azurite.
/// </summary>
public class AzureBlobObjectStorage : IObjectStorage, IStorageInitializer
{
    private readonly BlobServiceClient _service;
    private readonly BlobContainerClient _container;
    private readonly ILogger<AzureBlobObjectStorage> _logger;

    public AzureBlobObjectStorage(IOptions<StorageOptions> options, ILogger<AzureBlobObjectStorage> logger)
    {
        _logger = logger;
        var o = options.Value;
        if (!string.IsNullOrWhiteSpace(o.AzureBlob.ConnectionString))
        {
            _service = new BlobServiceClient(o.AzureBlob.ConnectionString);
        }
        else if (!string.IsNullOrWhiteSpace(o.AzureBlob.ServiceUri))
        {
            // AZURE_CLIENT_ID (set on the container app) selects the user-assigned managed identity.
            _service = new BlobServiceClient(new Uri(o.AzureBlob.ServiceUri), new DefaultAzureCredential());
        }
        else
        {
            throw new InvalidOperationException("Storage:AzureBlob needs either ServiceUri (managed identity) or ConnectionString.");
        }

        _container = _service.GetBlobContainerClient(o.Bucket);
    }

    public Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) =>
        _container.GetBlobClient(key).UploadAsync(content, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, cancellationToken);

    public async Task<Stream> GetAsync(string key, CancellationToken cancellationToken = default) =>
        await _container.GetBlobClient(key).OpenReadAsync(cancellationToken: cancellationToken);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken);

    public string GetDownloadUrl(string key, TimeSpan lifetime, string? downloadFileName = null)
    {
        var blob = _container.GetBlobClient(key);
        var expires = DateTimeOffset.UtcNow.Add(lifetime);
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, expires)
        {
            BlobContainerName = _container.Name,
            BlobName = key,
            Resource = "b",
        };
        if (downloadFileName is not null)
        {
            sas.ContentDisposition = $"attachment; filename=\"{downloadFileName.Replace("\"", string.Empty)}\"";
        }

        if (blob.CanGenerateSasUri)
        {
            // Shared-key credential (Azurite / connection string).
            return blob.GenerateSasUri(sas).ToString();
        }

        // Managed identity: sign with a user delegation key instead of an account key.
        var key2 = _service.GetUserDelegationKey(new BlobGetUserDelegationKeyOptions(expires) { StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5) });
        var uri = new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(key2.Value, _service.AccountName) };
        return uri.ToUri().ToString();
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return (await _container.ExistsAsync(cancellationToken)).Value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Object storage check failed: {Error}", ex.Message);
            return false;
        }
    }

    public async Task EnsureBucketAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning("Could not create storage container: {Error}", ex.Message);
            throw;
        }
    }
}

/// <summary>Storage that can create its own bucket/container at startup (local development).</summary>
public interface IStorageInitializer
{
    Task EnsureBucketAsync(CancellationToken cancellationToken = default);
}
