namespace SmartGym.Infrastructure.Services.FileStorage;

public class S3StorageOptions
{
    public const string SectionName = "S3Storage";

    public string BucketName { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string ServiceUrl { get; set; } = string.Empty;
    public bool UseLocalStorage { get; set; } = true;
}
