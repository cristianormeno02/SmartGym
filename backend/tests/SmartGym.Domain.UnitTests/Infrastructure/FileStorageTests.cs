using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;
using SmartGym.WebApi.Common;
using Microsoft.Extensions.Options;
using Moq;
using SmartGym.Infrastructure.Services.FileStorage;
using Xunit;

namespace SmartGym.Domain.UnitTests.Infrastructure;

public class FileStorageTests
{
    [Fact]
    public async Task LocalStorageService_GetAccessUrlAsync_ReturnsStoragePath()
    {
        var service = new LocalStorageService();
        var key = "avatars/123/pic.webp";

        var url = await service.GetAccessUrlAsync(key, TimeSpan.FromMinutes(15));

        Assert.Equal("/storage/avatars/123/pic.webp", url);
    }

    [Fact]
    public async Task LocalStorageService_GetAccessUrlAsync_WithLeadingSlashOrStorage_PreservesCleanPath()
    {
        var service = new LocalStorageService();
        var key = "/storage/avatars/123/pic.webp";

        var url = await service.GetAccessUrlAsync(key, TimeSpan.FromMinutes(15));

        Assert.Equal("/storage/avatars/123/pic.webp", url);
    }

    [Fact]
    public async Task S3StorageService_GetAccessUrlAsync_GeneratesPresignedUrl()
    {
        var mockS3 = new Mock<IAmazonS3>();
        mockS3.Setup(s => s.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()))
            .Returns("https://s3.amazonaws.com/smartgym-assets/avatars/123/pic.webp?token=xyz");

        var options = Options.Create(new S3StorageOptions
        {
            BucketName = "smartgym-assets",
            Region = "us-east-1"
        });

        var service = new S3StorageService(options, mockS3.Object);
        var url = await service.GetAccessUrlAsync("avatars/123/pic.webp", TimeSpan.FromMinutes(15));

        Assert.StartsWith("https://s3.amazonaws.com", url);
        Assert.Contains("avatars/123/pic.webp", url);
    }

    [Fact]
    public async Task LocalStorageService_GetAccessUrlAsync_DuringRequest_ReturnsAbsoluteUrlOnApiHost()
    {
        // El frontend corre en otro origen: una ruta relativa se resolvería contra su propio host.
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "http";
        httpContext.Request.Host = new HostString("localhost", 5017);
        var service = new LocalStorageService(new HttpContextAccessor { HttpContext = httpContext });

        var url = await service.GetAccessUrlAsync("/storage/avatars/123/pic.webp", TimeSpan.FromMinutes(15));

        Assert.Equal("http://localhost:5017/storage/avatars/123/pic.webp", url);
    }

    [Fact]
    public void LocalAvatarStaticFiles_ServesOnlyAvatarsFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smartgym-storage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "avatars", "p1"));
        Directory.CreateDirectory(Path.Combine(root, "medical-certificates", "p1"));
        File.WriteAllText(Path.Combine(root, "avatars", "p1", "a.webp"), "img");
        File.WriteAllText(Path.Combine(root, "medical-certificates", "p1", "c.pdf"), "pdf");

        try
        {
            var options = LocalAvatarStaticFiles.CreateOptions(root);

            Assert.Equal("/storage/avatars", options.RequestPath.Value);
            Assert.True(options.FileProvider!.GetFileInfo("p1/a.webp").Exists);
            // Los certificados médicos (datos de salud) nunca deben quedar expuestos como archivos estáticos.
            Assert.False(options.FileProvider.GetFileInfo("../medical-certificates/p1/c.pdf").Exists);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LocalPublicStorageService_GetPublicUrl_WithPublicBaseUrl_ReturnsComposedUrl()
    {
        var options = Options.Create(new PublicS3StorageOptions
        {
            PublicBaseUrl = "https://cdn.smartgym.com"
        });

        var service = new LocalPublicStorageService(options);
        var url = service.GetPublicUrl("activities/gallery/photo.webp");

        Assert.Equal("https://cdn.smartgym.com/activities/gallery/photo.webp", url);
    }

    [Fact]
    public void LocalPublicStorageService_GetPublicUrl_WithoutBaseUrl_ReturnsRelativePublicMedia()
    {
        var options = Options.Create(new PublicS3StorageOptions
        {
            PublicBaseUrl = ""
        });

        var service = new LocalPublicStorageService(options);
        var url = service.GetPublicUrl("activities/gallery/photo.webp");

        Assert.Equal("/public-media/activities/gallery/photo.webp", url);
    }
}
