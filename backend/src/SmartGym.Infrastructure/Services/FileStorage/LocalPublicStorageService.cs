using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common.Interfaces;

namespace SmartGym.Infrastructure.Services.FileStorage;

public class LocalPublicStorageService : IPublicFileStorageService
{
    public static readonly string DefaultRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "storage-public");

    private readonly string _baseStoragePath;
    private readonly string _publicBaseUrl;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public LocalPublicStorageService(
        IOptions<PublicS3StorageOptions> options,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _publicBaseUrl = options.Value.PublicBaseUrl?.TrimEnd('/') ?? string.Empty;
        _httpContextAccessor = httpContextAccessor;
        _baseStoragePath = DefaultRootPath;

        if (!Directory.Exists(_baseStoragePath))
        {
            Directory.CreateDirectory(_baseStoragePath);
        }
    }

    public async Task<string> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        var cleanFolder = folder.Trim('/');
        var targetFolder = Path.Combine(_baseStoragePath, cleanFolder);
        if (!Directory.Exists(targetFolder))
        {
            Directory.CreateDirectory(targetFolder);
        }

        var uniqueFileName = $"{Guid.NewGuid()}_{fileName}";
        var filePath = Path.Combine(targetFolder, uniqueFileName);

        await using var outputStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        await fileStream.CopyToAsync(outputStream, cancellationToken);

        return $"{cleanFolder}/{uniqueFileName}";
    }

    public Task<Stream?> DownloadFileAsync(
        string fileUrlOrKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = fileUrlOrKey.TrimStart('/').Replace("public-media/", string.Empty);
        var filePath = Path.Combine(_baseStoragePath, normalizedKey);

        if (!File.Exists(filePath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> DeleteFileAsync(
        string fileUrlOrKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = fileUrlOrKey.TrimStart('/').Replace("public-media/", string.Empty);
        var filePath = Path.Combine(_baseStoragePath, normalizedKey);

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
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
        var cleanKey = objectKey.TrimStart('/');
        if (!string.IsNullOrWhiteSpace(_publicBaseUrl))
        {
            return $"{_publicBaseUrl}/{cleanKey}";
        }

        var request = _httpContextAccessor?.HttpContext?.Request;
        if (request != null)
        {
            return $"{request.Scheme}://{request.Host}/public-media/{cleanKey}";
        }

        return $"/public-media/{cleanKey}";
    }
}
