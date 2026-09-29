using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Reservations.Dtos;

public record ReservationDto(
    Guid Id,
    Guid ClassSessionId,
    string ActivityName,
    DateOnly ClassDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string RoomName,
    Guid StudentId,
    string StudentName,
    string StudentDni,
    Guid? MembershipId,
    ReservationStatus Status,
    DateTime ReservedAtUtc,
    DateTime? ConfirmedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    bool IsLateCancellation,
    DateTime? AttendedAtUtc,
    AttendanceSource? AttendanceSource,
    int? WaitListPosition
);

public record CreateReservationRequest(
    Guid ClassSessionId,
    Guid StudentId
);

public record CancelReservationRequest(
    string Reason
);

public record RecordAttendanceRequest(
    Guid ReservationId,
    AttendanceSource Source
);

public record SessionAttendanceSummaryDto(
    Guid ClassSessionId,
    string ActivityName,
    DateOnly Date,
    int TotalCapacity,
    int ReservedCount,
    int AttendedCount,
    List<ReservationDto> Attendees
);
