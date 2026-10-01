using SkiaSharp;
using SmartGym.Application.Common.Interfaces;

namespace SmartGym.Infrastructure.Services.FileStorage;

public class ProfileImageProcessor : IProfileImageProcessor
{
    public const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    public const int MaxDimension = 1024;

    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] RiffHeader = [0x52, 0x49, 0x46, 0x46]; // "RIFF"
    private static readonly byte[] WebpHeader = [0x57, 0x45, 0x42, 0x50]; // "WEBP"

    public async Task<ProcessedImageResult> ProcessProfileImageAsync(
        Stream inputStream,
        string declaredContentType,
        long declaredLength,
        CancellationToken cancellationToken = default)
    {
        // 1. Validation: Size limit
        if (declaredLength > MaxFileSizeBytes)
        {
            throw new ArgumentException($"El archivo excede el tamaño máximo permitido de 5 MB ({MaxFileSizeBytes} bytes).", nameof(declaredLength));
        }

        // 2. Validation: MIME whitelist
        if (string.IsNullOrWhiteSpace(declaredContentType) || !AllowedMimeTypes.Contains(declaredContentType.Trim()))
        {
            throw new ArgumentException("El tipo de archivo no está permitido. Solo se aceptan imágenes JPEG, PNG o WebP.", nameof(declaredContentType));
        }

        // Read to memory stream for inspection and processing
        var memoryStream = new MemoryStream();
        await inputStream.CopyToAsync(memoryStream, cancellationToken);

        if (memoryStream.Length > MaxFileSizeBytes)
        {
            throw new ArgumentException($"El archivo excede el tamaño máximo permitido de 5 MB ({MaxFileSizeBytes} bytes).", nameof(inputStream));
        }

        if (memoryStream.Length < 12)
        {
            throw new ArgumentException("El archivo es demasiado pequeño o está dañado.", nameof(inputStream));
        }

        var bytes = memoryStream.ToArray();

        // 3. Validation: Magic-byte inspection
        if (!IsValidImageHeader(bytes))
        {
            throw new ArgumentException("El contenido del archivo no coincide con un formato de imagen permitido (firma mágica inválida).", nameof(inputStream));
        }

        // 4. Validation: Decodability via SkiaSharp
        memoryStream.Position = 0;
        using var originalBitmap = SKBitmap.Decode(memoryStream);
        if (originalBitmap == null)
        {
            throw new ArgumentException("La imagen está dañada o no se pudo decodificar.", nameof(inputStream));
        }

        // 5. Processing: Resize maintaining aspect ratio if > 1024x1024
        SKBitmap targetBitmap = originalBitmap;
        bool wasResized = false;

        if (originalBitmap.Width > MaxDimension || originalBitmap.Height > MaxDimension)
        {
            float ratio = Math.Min((float)MaxDimension / originalBitmap.Width, (float)MaxDimension / originalBitmap.Height);
            int targetWidth = Math.Max(1, (int)Math.Round(originalBitmap.Width * ratio));
            int targetHeight = Math.Max(1, (int)Math.Round(originalBitmap.Height * ratio));

            var info = new SKImageInfo(targetWidth, targetHeight, originalBitmap.ColorType, originalBitmap.AlphaType);
            var resized = originalBitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            if (resized != null)
            {
                targetBitmap = resized;
                wasResized = true;
            }
        }

        try
        {
            // 6. Processing: Re-encode to WebP (85% quality), completely stripping metadata (EXIF/GPS)
            using var image = SKImage.FromBitmap(targetBitmap);
            using var encodedData = image.Encode(SKEncodedImageFormat.Webp, 85);

            var outputStream = new MemoryStream();
            encodedData.SaveTo(outputStream);
            outputStream.Position = 0;

            return new ProcessedImageResult(
                outputStream,
                "image/webp",
                targetBitmap.Width,
                targetBitmap.Height,
                outputStream.Length
            );
        }
        finally
        {
            if (wasResized)
            {
                targetBitmap.Dispose();
            }
        }
    }

    private static bool IsValidImageHeader(byte[] bytes)
    {
        // JPEG: FF D8 FF
        if (bytes.Length >= 3 &&
            bytes[0] == JpegHeader[0] &&
            bytes[1] == JpegHeader[1] &&
            bytes[2] == JpegHeader[2])
        {
            return true;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytes.Length >= 8 &&
            bytes.Take(8).SequenceEqual(PngHeader))
        {
            return true;
        }

        // WebP: "RIFF" (0..3) and "WEBP" (8..11)
        if (bytes.Length >= 12 &&
            bytes.Take(4).SequenceEqual(RiffHeader) &&
            bytes.Skip(8).Take(4).SequenceEqual(WebpHeader))
        {
            return true;
        }

        return false;
    }
}
