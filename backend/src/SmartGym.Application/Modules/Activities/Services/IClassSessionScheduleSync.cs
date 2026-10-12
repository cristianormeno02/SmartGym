using SmartGym.Application.Modules.Activities.Dtos;

namespace SmartGym.Application.Modules.Activities.Services;

public interface IClassSessionScheduleSync
{
    Task<List<Guid>> SyncAsync(Guid scheduleId, DateTime? nowUtc = null, CancellationToken cancellationToken = default);
}
