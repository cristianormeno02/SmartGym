using SkiaSharp;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Infrastructure.Services.FileStorage;
using Xunit;

namespace SmartGym.Domain.UnitTests.Infrastructure;

public class ImageProcessorTests
{
    private readonly ProfileImageProcessor _processor = new();

    [Fact]
    public async Task ProcessImageAsync_WithActivityLogoProfile_ResizesToMax512()
    {
        var bytes = CreateImage(1024, 768, SKEncodedImageFormat.Jpeg);
        using var stream = new MemoryStream(bytes);

        var result = await _processor.ProcessImageAsync(
            stream,
            "image/jpeg",
            bytes.Length,
            ImageProcessingProfile.ActivityLogo);

        Assert.Equal("image/webp", result.ContentType);
        Assert.True(result.Width <= 512);
        Assert.True(result.Height <= 512);
        Assert.True(result.SizeInBytes > 0);
    }

    [Fact]
    public async Task ProcessImageAsync_WithActivityGalleryProfile_PreservesUnder1920()
    {
        var bytes = CreateImage(1200, 800, SKEncodedImageFormat.Png);
        using var stream = new MemoryStream(bytes);

        var result = await _processor.ProcessImageAsync(
            stream,
            "image/png",
            bytes.Length,
            ImageProcessingProfile.ActivityGallery);

        Assert.Equal("image/webp", result.ContentType);
        Assert.Equal(1200, result.Width);
        Assert.Equal(800, result.Height);
    }

    [Fact]
    public async Task ProcessImageAsync_WithExceededLogoSize_ThrowsArgumentException()
    {
        using var stream = new MemoryStream(new byte[100]);
        var declaredLength = ImageProcessingProfile.ActivityLogo.MaxFileSizeBytes + 1;

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _processor.ProcessImageAsync(stream, "image/jpeg", declaredLength, ImageProcessingProfile.ActivityLogo));

        Assert.Equal("declaredLength", ex.ParamName);
    }

    private static byte[] CreateImage(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.DarkBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 80);
        return data.ToArray();
    }
}
