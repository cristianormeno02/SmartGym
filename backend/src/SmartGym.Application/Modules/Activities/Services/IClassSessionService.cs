using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Activities.Services;

public interface IClassSessionService
{
    Task<int> GenerateSessionsFromSchedulesAsync(DateOnly fromDate, DateOnly toDate, Guid? recurringScheduleId = null, CancellationToken cancellationToken = default);
    Task<List<ClassSessionDto>> GetAllAsync(DateOnly? fromDate = null, DateOnly? toDate = null, Guid? activityId = null, Guid? instructorId = null, ClassSessionStatus? status = null, CancellationToken cancellationToken = default);
    Task<ClassSessionDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ClassSessionDto> CreateManualAsync(ManualCreateClassSessionRequest request, CancellationToken cancellationToken = default);
    Task<ClassSessionDto?> StartSessionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ClassSessionDto?> FinishSessionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ClassSessionDto?> SuspendSessionAsync(Guid id, string reason, CancellationToken cancellationToken = default);
    Task<ClassSessionDto?> CancelSessionAsync(Guid id, string reason, CancellationToken cancellationToken = default);
    Task<ClassSessionDto?> AssignSubstituteAsync(Guid id, Guid substituteInstructorId, CancellationToken cancellationToken = default);
}
