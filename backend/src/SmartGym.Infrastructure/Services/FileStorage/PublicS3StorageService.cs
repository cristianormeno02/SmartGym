using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common.Interfaces;

namespace SmartGym.Infrastructure.Services.FileStorage;

public class PublicS3StorageService : IPublicFileStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly PublicS3StorageOptions _options;

    public PublicS3StorageService(IOptions<PublicS3StorageOptions> options)
        : this(options, CreateClient(options.Value))
    {
    }

    public PublicS3StorageService(IOptions<PublicS3StorageOptions> options, IAmazonS3 s3Client)
    {
        _options = options.Value;
        _s3Client = s3Client;
    }

    private static IAmazonS3 CreateClient(PublicS3StorageOptions options)
    {
        var config = new AmazonS3Config();
        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
            config.ForcePathStyle = true;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        if (!string.IsNullOrWhiteSpace(options.AccessKey) && !string.IsNullOrWhiteSpace(options.SecretKey))
        {
            return new AmazonS3Client(options.AccessKey, options.SecretKey, config);
        }

        return new AmazonS3Client(config);
    }

    public async Task<string> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        var key = $"{folder.Trim('/')}/{Guid.NewGuid()}_{fileName}";

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = fileStream,
            ContentType = contentType,
            Headers =
            {
                CacheControl = "public, max-age=31536000, immutable"
            }
        };

        await _s3Client.PutObjectAsync(request, cancellationToken);
        return key;
    }

    public async Task<Stream?> DownloadFileAsync(
        string fileUrlOrKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _options.BucketName,
                Key = fileUrlOrKey
            };

            var response = await _s3Client.GetObjectAsync(request, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> DeleteFileAsync(
        string fileUrlOrKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new DeleteObjectRequest
            {
                BucketName = _options.BucketName,
                Key = fileUrlOrKey
            };

            await _s3Client.DeleteObjectAsync(request, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception)
        {
            return false;
        }
    }

    public Task<string> GetAccessUrlAsync(
        string fileUrlOrKey,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GetPublicUrl(fileUrlOrKey));
    }

    public string GetPublicUrl(string objectKey)
    {
        var baseUrl = _options.PublicBaseUrl?.TrimEnd('/') ?? string.Empty;
        var cleanKey = objectKey.TrimStart('/');
        return string.IsNullOrEmpty(baseUrl) ? cleanKey : $"{baseUrl}/{cleanKey}";
    }
}
