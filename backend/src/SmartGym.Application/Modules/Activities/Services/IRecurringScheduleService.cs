using SmartGym.Application.Modules.Activities.Dtos;

namespace SmartGym.Application.Modules.Activities.Services;

public interface IRecurringScheduleService
{
    Task<List<RecurringScheduleDto>> GetAllAsync(Guid? activityId = null, Guid? instructorId = null, CancellationToken cancellationToken = default);
    Task<RecurringScheduleDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RecurringScheduleDto> CreateAsync(CreateRecurringScheduleRequest request, CancellationToken cancellationToken = default);
    Task<RecurringScheduleDto?> UpdateAsync(Guid id, UpdateRecurringScheduleRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
