using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common.Interfaces;

namespace SmartGym.Infrastructure.Services.FileStorage;

public class S3StorageService : IFileStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly S3StorageOptions _options;

    public S3StorageService(IOptions<S3StorageOptions> options)
    {
        _options = options.Value;

        var config = new AmazonS3Config();
        if (!string.IsNullOrWhiteSpace(_options.ServiceUrl))
        {
            config.ServiceURL = _options.ServiceUrl;
            config.ForcePathStyle = true;
        }
        else
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(_options.Region);
        }

        if (!string.IsNullOrWhiteSpace(_options.AccessKey) && !string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            _s3Client = new AmazonS3Client(_options.AccessKey, _options.SecretKey, config);
        }
        else
        {
            _s3Client = new AmazonS3Client(config);
        }
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
            ContentType = contentType
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
}
