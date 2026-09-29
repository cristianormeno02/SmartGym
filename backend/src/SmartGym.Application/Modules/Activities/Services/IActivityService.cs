using SmartGym.Application.Modules.Activities.Dtos;

namespace SmartGym.Application.Modules.Activities.Services;

public interface IActivityService
{
    Task<List<ActivityDto>> GetAllAsync(bool onlyEnabled = false, CancellationToken cancellationToken = default);
    Task<ActivityDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ActivityDto> CreateAsync(CreateActivityRequest request, CancellationToken cancellationToken = default);
    Task<ActivityDto?> UpdateAsync(Guid id, UpdateActivityRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
