using SmartGym.Application.Common.Models;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Activities.Services;

public interface IActivityService
{
    Task<PagedResult<ActivityListItemDto>> GetPagedAsync(
        string? search,
        ActivityStatus? status,
        int? age,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);

    Task<ActivityDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ActivityDetailDto?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<ActivityDetailDto> CreateAsync(CreateActivityRequest request, CancellationToken cancellationToken = default);
    Task<ActivityDetailDto> UpdateAsync(Guid id, UpdateActivityRequest request, CancellationToken cancellationToken = default);
    Task<bool> ChangeStatusAsync(Guid id, ChangeActivityStatusRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<List<ActivityMediaDto>> GetMediaAsync(Guid activityId, CancellationToken cancellationToken = default);
    Task<ActivityMediaDto> UploadMediaAsync(Guid activityId, Stream fileStream, string fileName, string contentType, long sizeBytes, ActivityMediaType type, CancellationToken cancellationToken = default);
    Task DeleteMediaAsync(Guid activityId, Guid mediaId, CancellationToken cancellationToken = default);
    Task SetPrimaryMediaAsync(Guid activityId, Guid mediaId, CancellationToken cancellationToken = default);
    Task SortMediaAsync(Guid activityId, SortActivityMediaRequest request, CancellationToken cancellationToken = default);

    Task<List<PublicActivityDto>> GetPublicCatalogAsync(int? age = null, CancellationToken cancellationToken = default);
    Task<PublicActivityDto?> GetPublicByCodeAsync(string code, CancellationToken cancellationToken = default);
}
