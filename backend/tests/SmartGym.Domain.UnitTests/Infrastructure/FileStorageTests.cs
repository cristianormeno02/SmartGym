using Amazon.S3;
using Amazon.S3.Model;
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
}
