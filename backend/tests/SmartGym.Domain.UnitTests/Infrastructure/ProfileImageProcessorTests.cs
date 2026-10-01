using System.Text;
using SkiaSharp;
using SmartGym.Infrastructure.Services.FileStorage;
using Xunit;

namespace SmartGym.Domain.UnitTests.Infrastructure;

public class ProfileImageProcessorTests
{
    private readonly ProfileImageProcessor _processor = new();

    [Fact]
    public async Task ProcessProfileImageAsync_ValidJpeg_ProducesWebpResult()
    {
        var bytes = CreateImage(200, 200, SKEncodedImageFormat.Jpeg);
        using var stream = new MemoryStream(bytes);

        var result = await _processor.ProcessProfileImageAsync(stream, "image/jpeg", bytes.Length);

        Assert.Equal("image/webp", result.ContentType);
        Assert.Equal(200, result.Width);
        Assert.Equal(200, result.Height);
        Assert.True(result.SizeInBytes > 0);

        // Verify result can be decoded
        result.Stream.Position = 0;
        using var decoded = SKBitmap.Decode(result.Stream);
        Assert.NotNull(decoded);
        Assert.Equal(200, decoded.Width);
        Assert.Equal(200, decoded.Height);
    }

    [Fact]
    public async Task ProcessProfileImageAsync_ValidPng_ProducesWebpResult()
    {
        var bytes = CreateImage(150, 300, SKEncodedImageFormat.Png);
        using var stream = new MemoryStream(bytes);

        var result = await _processor.ProcessProfileImageAsync(stream, "image/png", bytes.Length);

        Assert.Equal("image/webp", result.ContentType);
        Assert.Equal(150, result.Width);
        Assert.Equal(300, result.Height);
    }

    [Fact]
    public async Task ProcessProfileImageAsync_ValidWebp_ProducesWebpResult()
    {
        var bytes = CreateImage(400, 400, SKEncodedImageFormat.Webp);
        using var stream = new MemoryStream(bytes);

        var result = await _processor.ProcessProfileImageAsync(stream, "image/webp", bytes.Length);

        Assert.Equal("image/webp", result.ContentType);
        Assert.Equal(400, result.Width);
        Assert.Equal(400, result.Height);
    }

    [Fact]
    public async Task ProcessProfileImageAsync_OversizedDimensions_ResizesToMax1024()
    {
        // 2000 x 1000 -> should resize to 1024 x 512
        var bytes = CreateImage(2000, 1000, SKEncodedImageFormat.Jpeg);
        using var stream = new MemoryStream(bytes);

        var result = await _processor.ProcessProfileImageAsync(stream, "image/jpeg", bytes.Length);

        Assert.Equal(1024, result.Width);
        Assert.Equal(512, result.Height);
    }

    [Fact]
    public async Task ProcessProfileImageAsync_PdfRenamedAsJpg_ThrowsArgumentException()
    {
        // PDF starts with %PDF- (0x25, 0x50, 0x44, 0x46)
        var fakePdfBytes = Encoding.ASCII.GetBytes("%PDF-1.5 fake pdf content that is not an image");
        using var stream = new MemoryStream(fakePdfBytes);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _processor.ProcessProfileImageAsync(stream, "image/jpeg", fakePdfBytes.Length));

        Assert.Contains("firma mágica", ex.Message);
    }

    [Fact]
    public async Task ProcessProfileImageAsync_OversizedFile_ThrowsArgumentException()
    {
        // Declared size > 5 MB
        long oversizedDeclared = 6 * 1024 * 1024;
        using var stream = new MemoryStream([1, 2, 3]);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _processor.ProcessProfileImageAsync(stream, "image/jpeg", oversizedDeclared));

        Assert.Contains("5 MB", ex.Message);
    }

    [Fact]
    public async Task ProcessProfileImageAsync_InvalidMimeType_ThrowsArgumentException()
    {
        var bytes = CreateImage(100, 100, SKEncodedImageFormat.Jpeg);
        using var stream = new MemoryStream(bytes);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _processor.ProcessProfileImageAsync(stream, "image/gif", bytes.Length));

        Assert.Contains("tipo de archivo no está permitido", ex.Message);
    }

    [Fact]
    public async Task ProcessProfileImageAsync_JpegWithExifGpsData_StripsExifMetadata()
    {
        // Create JPEG and inject APP1 (EXIF) segment containing GPS coordinates
        var baseJpeg = CreateImage(200, 200, SKEncodedImageFormat.Jpeg);

        // Inject APP1 marker: FF E1 <length: 2 bytes> "Exif\0\0" ... "GPSLatitude: -34.6037"
        var exifPayload = Encoding.ASCII.GetBytes("Exif\0\0GPSLatitude: -34.6037, GPSLongitude: -58.3816");
        var app1Marker = new byte[] { 0xFF, 0xE1, (byte)((exifPayload.Length + 2) >> 8), (byte)((exifPayload.Length + 2) & 0xFF) };

        var jpegWithGps = new byte[baseJpeg.Length + app1Marker.Length + exifPayload.Length];
        // Copy SOI (FF D8)
        Array.Copy(baseJpeg, 0, jpegWithGps, 0, 2);
        // Copy APP1 marker & Exif payload
        Array.Copy(app1Marker, 0, jpegWithGps, 2, app1Marker.Length);
        Array.Copy(exifPayload, 0, jpegWithGps, 2 + app1Marker.Length, exifPayload.Length);
        // Copy the rest of the original JPEG
        Array.Copy(baseJpeg, 2, jpegWithGps, 2 + app1Marker.Length + exifPayload.Length, baseJpeg.Length - 2);

        // Verify the injected payload is actually in the test input
        var inputString = Encoding.ASCII.GetString(jpegWithGps);
        Assert.Contains("GPSLatitude", inputString);

        using var stream = new MemoryStream(jpegWithGps);
        var result = await _processor.ProcessProfileImageAsync(stream, "image/jpeg", jpegWithGps.Length);

        // Verify that the output WebP has stripped all EXIF and GPS markers
        result.Stream.Position = 0;
        using var memOut = new MemoryStream();
        await result.Stream.CopyToAsync(memOut);
        var outputBytes = memOut.ToArray();
        var outputString = Encoding.ASCII.GetString(outputBytes);

        Assert.DoesNotContain("GPSLatitude", outputString);
        Assert.DoesNotContain("Exif", outputString);
        Assert.DoesNotContain("GPS", outputString);
    }

    private static byte[] CreateImage(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Crimson);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 80);
        return data.ToArray();
    }
}
