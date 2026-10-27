using SmartGym.Application.Common.Models;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Reservations.Services;

public interface IReservationService
{
    Task<ReservationDto> CreateReservationAsync(CreateReservationRequest request, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<ReservationDto> CancelReservationAsync(Guid reservationId, CancelReservationRequest request, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<ReservationDto> JoinWaitlistAsync(Guid classSessionId, JoinWaitlistRequest request, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<ReservationDto> LeaveWaitlistAsync(Guid classSessionId, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<ReservationDto> AcceptWaitlistOfferAsync(Guid reservationId, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<ReservationDto> DeclineWaitlistOfferAsync(Guid reservationId, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<int> PromoteWaitlistAsync(ClassSession session, Guid? actorUserId = null, CancellationToken cancellationToken = default);
    Task CompactWaitlistPositionsAsync(Guid classSessionId, CancellationToken cancellationToken = default);
    Task<WaitlistProcessResultDto> ProcessWaitlistAsync(CancellationToken cancellationToken = default);
    Task<ReservationDto> RecordAttendanceAsync(RecordAttendanceRequest request, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<ReservationDto> RecordNoShowAsync(Guid reservationId, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<PagedResult<ReservationDto>> GetPagedAsync(ReservationFilterRequest request, CancellationToken cancellationToken = default);
    Task<ReservationDetailDto> GetByIdAsync(Guid reservationId, CancellationToken cancellationToken = default);
    Task<List<ReservationDto>> GetByStudentAsync(Guid studentId, CancellationToken cancellationToken = default);
    Task<List<ReservationDto>> GetByStudentAsync(Guid studentId, StudentReservationHistoryFilterRequest? filter, CancellationToken cancellationToken = default);
    Task<List<ReservationDto>> GetMyReservationsAsync(StudentReservationHistoryFilterRequest? filter = null, CancellationToken cancellationToken = default);
    Task<List<ReservationDto>> GetMyUpcomingAsync(CancellationToken cancellationToken = default);
    Task<List<ReservationDto>> GetByClassSessionAsync(Guid classSessionId, CancellationToken cancellationToken = default);
    Task<List<WaitlistEntryDto>> GetWaitlistAsync(Guid classSessionId, CancellationToken cancellationToken = default);
    Task<SessionAttendanceSummaryDto> GetSessionAttendanceSummaryAsync(Guid classSessionId, CancellationToken cancellationToken = default);
    Task<List<ReservableClassSessionDto>> GetAvailableClassesAsync(AvailableClassesFilterRequest request, CancellationToken cancellationToken = default);
    Task<DeskEligibilityDto> GetDeskEligibilityAsync(Guid classSessionId, Guid studentId, CancellationToken cancellationToken = default);
    Task<DeskDropInResponse> CheckInDeskDropInAsync(DeskDropInRequest request, Guid staffUserId, CancellationToken cancellationToken = default);
    Task<RevokeAttendanceResponse> RevokeAttendanceAsync(Guid reservationId, RevokeAttendanceRequest request, Guid staffUserId, CancellationToken cancellationToken = default);
}
