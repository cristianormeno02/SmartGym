using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ActivityMediaTests
{
    [Fact]
    public void Constructor_WithValidData_ShouldInitializeProperties()
    {
        var activityId = Guid.NewGuid();
        var media = new ActivityMedia(
            activityId: activityId,
            type: ActivityMediaType.GalleryImage,
            objectKey: "activities/media/image-1.webp",
            originalFileName: @"C:\Users\Test\Pictures\photo.jpg",
            contentType: "image/webp",
            sizeBytes: 102400,
            width: 1920,
            height: 1080,
            sortOrder: 1);

        Assert.Equal(activityId, media.ActivityId);
        Assert.Equal(ActivityMediaType.GalleryImage, media.Type);
        Assert.Equal("activities/media/image-1.webp", media.ObjectKey);
        Assert.Equal("photo.jpg", media.OriginalFileName);
        Assert.Equal("image/webp", media.ContentType);
        Assert.Equal(102400, media.SizeBytes);
        Assert.Equal(1920, media.Width);
        Assert.Equal(1080, media.Height);
        Assert.Equal(1, media.SortOrder);
        Assert.False(media.IsPrimary);
    }

    [Fact]
    public void Constructor_WithNonPositiveSizeBytes_ShouldThrowArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => new ActivityMedia(
            Guid.NewGuid(),
            ActivityMediaType.GalleryImage,
            "key",
            "file.png",
            "image/webp",
            sizeBytes: 0,
            width: 100,
            height: 100,
            sortOrder: 0));

        Assert.Equal("sizeBytes", ex.ParamName);
    }

    [Fact]
    public void SetPrimary_True_ForLogo_ShouldThrowInvalidOperationException()
    {
        var media = new ActivityMedia(
            Guid.NewGuid(),
            ActivityMediaType.Logo,
            "key",
            "logo.png",
            "image/webp",
            sizeBytes: 5000,
            width: 256,
            height: 256,
            sortOrder: 0);

        var ex = Assert.Throws<InvalidOperationException>(() => media.SetPrimary(true));
        Assert.Contains("Solo una imagen de galería puede ser principal", ex.Message);
    }

    [Fact]
    public void SetPrimary_True_ForGalleryImage_ShouldSucceed()
    {
        var media = new ActivityMedia(
            Guid.NewGuid(),
            ActivityMediaType.GalleryImage,
            "key",
            "img.png",
            "image/webp",
            sizeBytes: 5000,
            width: 1024,
            height: 768,
            sortOrder: 1);

        media.SetPrimary(true);
        Assert.True(media.IsPrimary);
        Assert.NotNull(media.UpdatedAtUtc);

        media.SetPrimary(false);
        Assert.False(media.IsPrimary);
    }

    [Fact]
    public void SetSortOrder_ShouldUpdateSortOrderAndTimestamp()
    {
        var media = new ActivityMedia(
            Guid.NewGuid(),
            ActivityMediaType.GalleryImage,
            "key",
            "img.png",
            "image/webp",
            sizeBytes: 5000,
            width: 1024,
            height: 768,
            sortOrder: 1);

        media.SetSortOrder(5);
        Assert.Equal(5, media.SortOrder);
        Assert.NotNull(media.UpdatedAtUtc);
    }
}
