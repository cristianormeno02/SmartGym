namespace SmartGym.Application.Common.Interfaces;

public record ImageProcessingProfile(
    long MaxFileSizeBytes,
    int MaxDimension,
    long MaxPixels = 50_000_000,
    int Quality = 85
)
{
    public static readonly ImageProcessingProfile Profile = new(5 * 1024 * 1024, 1024);
}

public interface IImageProcessor
{
    Task<ProcessedImageResult> ProcessImageAsync(
        Stream inputStream,
        string declaredContentType,
        long declaredLength,
        ImageProcessingProfile profile,
        CancellationToken cancellationToken = default);
}
