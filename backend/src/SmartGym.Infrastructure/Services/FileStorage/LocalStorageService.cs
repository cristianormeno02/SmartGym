using SmartGym.Application.Common.Interfaces;

namespace SmartGym.Infrastructure.Services.FileStorage;

public class LocalStorageService : IFileStorageService
{
    private readonly string _baseStoragePath;

    public LocalStorageService()
    {
        _baseStoragePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Storage");
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
        var targetFolder = Path.Combine(_baseStoragePath, folder);
        if (!Directory.Exists(targetFolder))
        {
            Directory.CreateDirectory(targetFolder);
        }

        var uniqueFileName = $"{Guid.NewGuid()}_{fileName}";
        var filePath = Path.Combine(targetFolder, uniqueFileName);

        await using var outputStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        await fileStream.CopyToAsync(outputStream, cancellationToken);

        return $"/storage/{folder}/{uniqueFileName}";
    }

    public Task<Stream?> DownloadFileAsync(
        string fileUrlOrKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = fileUrlOrKey.TrimStart('/').Replace("storage/", string.Empty);
        var filePath = Path.Combine(_baseStoragePath, normalizedKey);

        if (!File.Exists(filePath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        return Task.FromResult<Stream?>(stream);
    }

    public Task<bool> DeleteFileAsync(
        string fileUrlOrKey,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = fileUrlOrKey.TrimStart('/').Replace("storage/", string.Empty);
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
        var normalizedKey = fileUrlOrKey.Trim();
        if (normalizedKey.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(normalizedKey);
        }

        if (normalizedKey.StartsWith("storage/", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult($"/{normalizedKey}");
        }

        var path = normalizedKey.TrimStart('/');
        return Task.FromResult($"/storage/{path}");
    }
}
