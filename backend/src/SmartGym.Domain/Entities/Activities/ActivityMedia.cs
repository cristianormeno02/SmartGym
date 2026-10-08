using System.IO;
using SmartGym.Domain.Common;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Activities;

public class ActivityMedia : BaseEntity
{
    public Guid ActivityId { get; private set; }
    public Activity Activity { get; private set; } = null!;
    public ActivityMediaType Type { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsPrimary { get; private set; }

    private ActivityMedia() { }

    public ActivityMedia(
        Guid activityId,
        ActivityMediaType type,
        string objectKey,
        string originalFileName,
        string contentType,
        long sizeBytes,
        int width,
        int height,
        int sortOrder)
    {
        if (sizeBytes <= 0)
        {
            throw new ArgumentException("El tamaño debe ser positivo.", nameof(sizeBytes));
        }

        ActivityId = activityId;
        Type = type;
        ObjectKey = objectKey;
        OriginalFileName = Path.GetFileName(originalFileName);
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Width = width;
        Height = height;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public void SetPrimary(bool isPrimary)
    {
        if (isPrimary && Type != ActivityMediaType.GalleryImage)
        {
            throw new InvalidOperationException("Solo una imagen de galería puede ser principal.");
        }

        IsPrimary = isPrimary;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetSortOrder(int sortOrder)
    {
        SortOrder = sortOrder;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
