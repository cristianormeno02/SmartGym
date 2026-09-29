namespace SmartGym.Application.Common.Interfaces;

public interface IFileStorageService
{
    Task<string> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        string folder,
        CancellationToken cancellationToken = default);

    Task<Stream?> DownloadFileAsync(
        string fileUrlOrKey,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteFileAsync(
        string fileUrlOrKey,
        CancellationToken cancellationToken = default);
}
