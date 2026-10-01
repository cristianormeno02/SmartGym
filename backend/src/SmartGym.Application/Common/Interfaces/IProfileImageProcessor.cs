namespace SmartGym.Application.Common.Interfaces;

public record ProcessedImageResult(
    Stream Stream,
    string ContentType,
    int Width,
    int Height,
    long SizeInBytes
);

public interface IProfileImageProcessor
{
    Task<ProcessedImageResult> ProcessProfileImageAsync(
        Stream inputStream,
        string declaredContentType,
        long declaredLength,
        CancellationToken cancellationToken = default);
}
