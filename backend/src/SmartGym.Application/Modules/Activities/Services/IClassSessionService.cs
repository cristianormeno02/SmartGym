using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartGym.Application.Common.Models;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Activities.Services;

public interface IClassSessionService
{
    Task<GenerationResultDto> GenerateSessionsAsync(GenerateSessionsRequest request, CancellationToken cancellationToken = default);
    Task<PagedResult<ClassSessionListItemDto>> GetPagedAsync(
        DateOnly? from = null,
        DateOnly? to = null,
        Guid? activityId = null,
        Guid? roomId = null,
        Guid? instructorId = null,
        Guid? secretaryId = null,
        ClassSessionDisplayStatus? status = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);
    Task<ClassSessionDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<ClassSessionAvailabilityDto>> GetAvailableAsync(DateOnly? from = null, DateOnly? to = null, Guid? activityId = null, CancellationToken cancellationToken = default);
    Task<ClassSessionDetailDto> CreateExtraordinaryAsync(CreateClassSessionRequest request, CancellationToken cancellationToken = default);
    Task<ClassSessionDetailDto> UpdateAsync(Guid id, UpdateClassSessionRequest request, CancellationToken cancellationToken = default);
    Task<ClassSessionDetailDto> OpenAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ClassSessionDetailDto> SuspendAsync(Guid id, SuspendClassSessionRequest request, CancellationToken cancellationToken = default);
    Task<ClassSessionDetailDto> CancelAsync(Guid id, CancelClassSessionRequest request, CancellationToken cancellationToken = default);
    Task<ClassSessionDetailDto> RescheduleAsync(Guid id, RescheduleClassSessionRequest request, CancellationToken cancellationToken = default);
    Task<ClassSessionDetailDto> ReplaceInstructorAsync(Guid id, ReplaceStaffRequest request, CancellationToken cancellationToken = default);
    Task<ClassSessionDetailDto> ReplaceSecretaryAsync(Guid id, ReplaceStaffRequest request, CancellationToken cancellationToken = default);
    Task<ClassSessionDetailDto> ChangeRoomAsync(Guid id, ChangeRoomRequest request, CancellationToken cancellationToken = default);
}
