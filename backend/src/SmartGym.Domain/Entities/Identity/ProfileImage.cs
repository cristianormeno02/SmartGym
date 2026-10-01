namespace SmartGym.Domain.Entities.Identity;

public class ProfileImage
{
    public string Key { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public DateTime UploadedAtUtc { get; private set; }

    private ProfileImage() { }

    private ProfileImage(string key, string contentType, long sizeBytes, DateTime uploadedAtUtc)
    {
        Key = key;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        UploadedAtUtc = uploadedAtUtc;
    }

    public static ProfileImage Create(
        string key,
        string contentType,
        long sizeBytes,
        DateTime? uploadedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Profile image key cannot be empty.", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("Profile image content type cannot be empty.", nameof(contentType));
        }

        if (sizeBytes <= 0)
        {
            throw new ArgumentException("Profile image size must be greater than zero.", nameof(sizeBytes));
        }

        return new ProfileImage(
            key.Trim(),
            contentType.Trim().ToLowerInvariant(),
            sizeBytes,
            uploadedAtUtc ?? DateTime.UtcNow);
    }
}
