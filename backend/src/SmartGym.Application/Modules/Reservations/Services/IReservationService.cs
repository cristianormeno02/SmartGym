using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Reservations.Services;

public interface IReservationService
{
    Task<ReservationDto> CreateReservationAsync(CreateReservationRequest request, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<ReservationDto> CancelReservationAsync(Guid reservationId, CancelReservationRequest request, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<ReservationDto> RecordAttendanceAsync(RecordAttendanceRequest request, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<ReservationDto> RecordNoShowAsync(Guid reservationId, Guid currentUserId, CancellationToken cancellationToken = default);
    Task<List<ReservationDto>> GetByStudentAsync(Guid studentId, CancellationToken cancellationToken = default);
    Task<List<ReservationDto>> GetByClassSessionAsync(Guid classSessionId, CancellationToken cancellationToken = default);
    Task<SessionAttendanceSummaryDto> GetSessionAttendanceSummaryAsync(Guid classSessionId, CancellationToken cancellationToken = default);
}
