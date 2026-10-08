namespace SmartGym.Application.Common.Interfaces;

public interface IPublicFileStorageService : IFileStorageService
{
    string GetPublicUrl(string objectKey);
}
