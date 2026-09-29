using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Activities.Dtos;

public record ClassSessionDto(
    Guid Id,
    Guid? RecurringScheduleId,
    Guid ActivityId,
    string ActivityName,
    Guid RoomId,
    string RoomName,
    Guid InstructorId,
    string InstructorName,
    Guid? OriginalInstructorId,
    string? OriginalInstructorName,
    Guid? SubstituteInstructorId,
    string? SubstituteInstructorName,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int MaxCapacity,
    int ReservedCount,
    int AttendedCount,
    int AvailableSpots,
    ClassSessionStatus Status,
    string? CancellationReason,
    DateTime? SuspendedAtUtc
);

public record GenerateSessionsRequest(
    DateOnly FromDate,
    DateOnly ToDate,
    Guid? RecurringScheduleId
);

public record ManualCreateClassSessionRequest(
    Guid ActivityId,
    Guid RoomId,
    Guid InstructorId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int MaxCapacity
);

public record SuspendClassSessionRequest(
    string Reason
);

public record CancelClassSessionRequest(
    string Reason
);

public record SubstituteInstructorRequest(
    Guid SubstituteInstructorId
);
