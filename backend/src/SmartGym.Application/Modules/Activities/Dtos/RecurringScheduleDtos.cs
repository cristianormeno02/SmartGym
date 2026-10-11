namespace SmartGym.Application.Modules.Activities.Dtos;

public record RecurringScheduleDto(
    Guid Id,
    Guid ActivityId,
    string ActivityName,
    Guid RoomId,
    string RoomName,
    Guid InstructorId,
    string InstructorName,
    Guid? SecretaryId,
    string? SecretaryName,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    int? MaxCapacity,
    bool IsActive,
    IReadOnlyList<Guid>? DivergentSessionIds = null
);

public record CreateRecurringScheduleRequest(
    Guid ActivityId,
    Guid RoomId,
    Guid InstructorId,
    Guid? SecretaryId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    int? MaxCapacity
);

public record UpdateRecurringScheduleRequest(
    Guid RoomId,
    Guid InstructorId,
    Guid? SecretaryId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    int? MaxCapacity,
    bool IsActive
);
