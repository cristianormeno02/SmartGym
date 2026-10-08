using SkiaSharp;
using SmartGym.Application.Common.Interfaces;

namespace SmartGym.Infrastructure.Services.FileStorage;

public class ProfileImageProcessor : IProfileImageProcessor, IImageProcessor
{
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

    public Task<ProcessedImageResult> ProcessProfileImageAsync(
        Stream inputStream,
        string declaredContentType,
        long declaredLength,
        CancellationToken cancellationToken = default)
    {
        return ProcessImageAsync(inputStream, declaredContentType, declaredLength, ImageProcessingProfile.Profile, cancellationToken);
    }

    public async Task<ProcessedImageResult> ProcessImageAsync(
        Stream inputStream,
        string declaredContentType,
        long declaredLength,
        ImageProcessingProfile profile,
        CancellationToken cancellationToken = default)
    {
        // 1. Validation: Size limit
        if (declaredLength > profile.MaxFileSizeBytes)
        {
            throw new ArgumentException($"El archivo excede el tamaño máximo permitido de {profile.MaxFileSizeBytes / (1024 * 1024)} MB ({profile.MaxFileSizeBytes} bytes).", nameof(declaredLength));
        }

        // 2. Validation: MIME whitelist
        if (string.IsNullOrWhiteSpace(declaredContentType) || !AllowedMimeTypes.Contains(declaredContentType.Trim()))
        {
            throw new ArgumentException("El tipo de archivo no está permitido. Solo se aceptan imágenes JPEG, PNG o WebP.", nameof(declaredContentType));
        }

        // Read to memory stream for inspection and processing
        var memoryStream = new MemoryStream();
        await inputStream.CopyToAsync(memoryStream, cancellationToken);

        if (memoryStream.Length > profile.MaxFileSizeBytes)
        {
            throw new ArgumentException($"El archivo excede el tamaño máximo permitido de {profile.MaxFileSizeBytes / (1024 * 1024)} MB ({profile.MaxFileSizeBytes} bytes).", nameof(inputStream));
        }

        if (memoryStream.Length < 12)
        {
            throw new ArgumentException("El archivo es demasiado pequeño o está dañado.", nameof(inputStream));
        }

        var bytes = memoryStream.ToArray();

        // 3. Validation: Magic-byte inspection, coherent with the declared content type
        var detectedContentType = DetectContentType(bytes);
        if (detectedContentType == null)
        {
            throw new ArgumentException("El contenido del archivo no coincide con un formato de imagen permitido (firma mágica inválida).", nameof(inputStream));
        }

        if (!string.Equals(detectedContentType, declaredContentType.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"El contenido del archivo ({detectedContentType}) no coincide con el tipo declarado ({declaredContentType.Trim()}).", nameof(declaredContentType));
        }

        // 4. Validation: dimensions declared in the header (before allocating pixels) and decodability
        using var sourceData = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(sourceData);
        if (codec == null)
        {
            throw new ArgumentException("La imagen está dañada o no se pudo decodificar.", nameof(inputStream));
        }

        if ((long)codec.Info.Width * codec.Info.Height > profile.MaxPixels)
        {
            throw new ArgumentException(
                $"La imagen supera el máximo de {profile.MaxPixels / 1_000_000} megapíxeles ({codec.Info.Width}x{codec.Info.Height}).",
                nameof(inputStream));
        }

        using var decodedBitmap = SKBitmap.Decode(codec);
        if (decodedBitmap == null)
        {
            throw new ArgumentException("La imagen está dañada o no se pudo decodificar.", nameof(inputStream));
        }

        // La orientación EXIF se pierde al descartar la metadata: se aplica a los píxeles antes de reencodear.
        using var orientedBitmap = ApplyEncodedOrigin(decodedBitmap, codec.EncodedOrigin);
        var originalBitmap = orientedBitmap ?? decodedBitmap;

        // 5. Processing: Resize maintaining aspect ratio if exceeding max dimensions
        SKBitmap targetBitmap = originalBitmap;
        bool wasResized = false;

        if (originalBitmap.Width > profile.MaxDimension || originalBitmap.Height > profile.MaxDimension)
        {
            float ratio = Math.Min((float)profile.MaxDimension / originalBitmap.Width, (float)profile.MaxDimension / originalBitmap.Height);
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
            // 6. Processing: Re-encode to WebP (profile.Quality), completely stripping metadata (EXIF/GPS)
            using var image = SKImage.FromBitmap(targetBitmap);
            using var encodedData = image.Encode(SKEncodedImageFormat.Webp, profile.Quality);

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

    private static string? DetectContentType(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == JpegHeader[0] && bytes[1] == JpegHeader[1] && bytes[2] == JpegHeader[2])
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 8 && bytes.Take(8).SequenceEqual(PngHeader))
        {
            return "image/png";
        }

        if (bytes.Length >= 12 &&
            bytes.Take(4).SequenceEqual(RiffHeader) &&
            bytes.Skip(8).Take(4).SequenceEqual(WebpHeader))
        {
            return "image/webp";
        }

        return null;
    }

    private static SKBitmap? ApplyEncodedOrigin(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft || origin == SKEncodedOrigin.Default)
        {
            return null;
        }

        var isNinetyOr270 = origin is SKEncodedOrigin.LeftTop
            or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom
            or SKEncodedOrigin.LeftBottom;

        var rotatedInfo = new SKImageInfo(
            isNinetyOr270 ? bitmap.Height : bitmap.Width,
            isNinetyOr270 ? bitmap.Width : bitmap.Height,
            bitmap.ColorType,
            bitmap.AlphaType);

        var rotated = new SKBitmap(rotatedInfo);
        using var canvas = new SKCanvas(rotated);

        switch (origin)
        {
            case SKEncodedOrigin.TopRight: // horizontal flip
                canvas.Scale(-1, 1, bitmap.Width / 2f, 0);
                break;
            case SKEncodedOrigin.BottomRight: // 180 rotate
                canvas.RotateDegrees(180, bitmap.Width / 2f, bitmap.Height / 2f);
                break;
            case SKEncodedOrigin.BottomLeft: // vertical flip
                canvas.Scale(1, -1, 0, bitmap.Height / 2f);
                break;
            case SKEncodedOrigin.LeftTop: // transpose
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop: // 90 rotate clockwise
                canvas.RotateDegrees(90);
                canvas.Translate(0, -bitmap.Height);
                break;
            case SKEncodedOrigin.RightBottom: // transverse
                canvas.RotateDegrees(270);
                canvas.Scale(1, -1);
                canvas.Translate(0, -bitmap.Height);
                break;
            case SKEncodedOrigin.LeftBottom: // 270 rotate clockwise (90 counter-clockwise)
                canvas.RotateDegrees(270);
                canvas.Translate(-bitmap.Width, 0);
                break;
        }

        canvas.DrawBitmap(bitmap, 0, 0);
        return rotated;
    }
}
