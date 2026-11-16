using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nichotan.Application.Common;

namespace Nichotan.Infrastructure.Storage;

public class StorageOptions
{
    /// <summary>S3 endpoint for S3-compatible hosts (R2, B2, Spaces, SeaweedFS…). Leave empty for AWS S3.</summary>
    public string? ServiceUrl { get; set; }

    public string Region { get; set; } = "us-east-1";
    public string Bucket { get; set; } = "nichotan-documents";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Path-style URLs (bucket in the path), needed by most self-hosted S3 servers.</summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>Create the bucket at startup if it's missing (local development).</summary>
    public bool CreateBucketIfMissing { get; set; }
}

/// <summary>
/// Private object storage over the S3 API. Objects are written without public ACLs and only reachable through
/// short-lived presigned URLs or the API; the database keeps metadata and keys, never file contents.
/// </summary>
public class S3ObjectStorage : IObjectStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly StorageOptions _options;
    private readonly ILogger<S3ObjectStorage> _logger;

    public S3ObjectStorage(IOptions<StorageOptions> options, ILogger<S3ObjectStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
        var config = new AmazonS3Config { ForcePathStyle = _options.ForcePathStyle, AuthenticationRegion = _options.Region };
        if (!string.IsNullOrEmpty(_options.ServiceUrl)) config.ServiceURL = _options.ServiceUrl;
        else config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(_options.Region);
        _client = new AmazonS3Client(new BasicAWSCredentials(_options.AccessKey, _options.SecretKey), config);
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        }, cancellationToken);
    }

    public async Task<Stream> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetObjectAsync(_options.Bucket, key, cancellationToken);
        return response.ResponseStream;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        _client.DeleteObjectAsync(_options.Bucket, key, cancellationToken);

    public string GetDownloadUrl(string key, TimeSpan lifetime, string? downloadFileName = null)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(lifetime),
            Protocol = _options.ServiceUrl?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true ? Protocol.HTTP : Protocol.HTTPS,
        };
        if (downloadFileName is not null)
        {
            request.ResponseHeaderOverrides.ContentDisposition = $"attachment; filename=\"{downloadFileName.Replace("\"", string.Empty)}\"";
        }

        return _client.GetPreSignedURL(request);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = _options.Bucket, MaxKeys = 1 }, cancellationToken);
            return true;
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
            await _client.PutBucketAsync(new PutBucketRequest { BucketName = _options.Bucket }, cancellationToken);
            _logger.LogInformation("Created storage bucket {Bucket}", _options.Bucket);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
        }
    }

    public void Dispose() => _client.Dispose();
}
