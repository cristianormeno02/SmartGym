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

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/png")]
    [InlineData(SKEncodedImageFormat.Webp, "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/webp")]
    public async Task ProcessProfileImageAsync_SignatureDoesNotMatchDeclaredType_ThrowsArgumentException(
        SKEncodedImageFormat actualFormat, string declaredContentType)
    {
        var bytes = CreateImage(50, 50, actualFormat);
        using var stream = new MemoryStream(bytes);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _processor.ProcessProfileImageAsync(stream, declaredContentType, bytes.Length));
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

    [Fact]
    public async Task ProcessProfileImageAsync_JpegWithExifOrientation_AppliesRotationBeforeStrippingMetadata()
    {
        // Foto "acostada" de 200x100 (izquierda roja, derecha azul) con EXIF Orientation = 6 (rotar 90° horario).
        // Al quitar el EXIF sin aplicar la rotación, la foto quedaría acostada.
        var bytes = InsertExifOrientation(CreateTwoColorImage(200, 100), orientation: 6);
        using var stream = new MemoryStream(bytes);

        var result = await _processor.ProcessProfileImageAsync(stream, "image/jpeg", bytes.Length);

        Assert.Equal(100, result.Width);
        Assert.Equal(200, result.Height);

        result.Stream.Position = 0;
        using var decoded = SKBitmap.Decode(result.Stream);
        var top = decoded.GetPixel(50, 20);
        var bottom = decoded.GetPixel(50, 180);
        Assert.True(top.Red > 200 && top.Blue < 60, $"Arriba debería quedar el rojo y quedó {top}");
        Assert.True(bottom.Blue > 200 && bottom.Red < 60, $"Abajo debería quedar el azul y quedó {bottom}");
    }

    [Fact]
    public async Task ProcessProfileImageAsync_DeclaredDimensionsAboveLimit_RejectsBeforeDecoding()
    {
        // Archivo de pocos bytes que declara 20000x20000 px: decodificarlo reservaría ~1.6 GB de memoria.
        var bytes = CreatePngDeclaringDimensions(20000, 20000);
        using var stream = new MemoryStream(bytes);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _processor.ProcessProfileImageAsync(stream, "image/png", bytes.Length));

        Assert.Contains("megapíxeles", ex.Message);
    }

    private static byte[] CreateTwoColorImage(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var red = new SKPaint { Color = SKColors.Red };
        using var blue = new SKPaint { Color = SKColors.Blue };
        canvas.DrawRect(0, 0, width / 2f, height, red);
        canvas.DrawRect(width / 2f, 0, width / 2f, height, blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        return data.ToArray();
    }

    private static byte[] InsertExifOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] payload =
        [
            .. Encoding.ASCII.GetBytes("Exif  "),
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,             // TIFF little-endian, IFD0 en offset 8
            0x01, 0x00,                                                 // 1 entrada
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00,             // tag 0x0112 Orientation, SHORT, count 1
            (byte)orientation, (byte)(orientation >> 8), 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00                                      // sin IFD siguiente
        ];
        var length = payload.Length + 2;
        byte[] app1 = [0xFF, 0xE1, (byte)(length >> 8), (byte)(length & 0xFF), .. payload];

        return [.. jpeg[..2], .. app1, .. jpeg[2..]];
    }

    private static byte[] CreatePngDeclaringDimensions(int width, int height)
    {
        var png = CreateImage(1, 1, SKEncodedImageFormat.Png);

        // IHDR: longitud(8..11) tipo(12..15) ancho(16..19) alto(20..23) ... CRC(29..32) sobre tipo+datos(12..28)
        WriteBigEndian(png, 16, (uint)width);
        WriteBigEndian(png, 20, (uint)height);
        WriteBigEndian(png, 29, Crc32(png.AsSpan(12, 17)));
        return png;
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
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
