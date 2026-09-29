namespace SmartGym.Application.Modules.Activities.Dtos;

public record RecurringScheduleDto(
    Guid Id,
    Guid ActivityId,
    string ActivityName,
    Guid RoomId,
    string RoomName,
    Guid InstructorId,
    string InstructorName,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    int? MaxCapacity,
    bool IsActive
);

public record CreateRecurringScheduleRequest(
    Guid ActivityId,
    Guid RoomId,
    Guid InstructorId,
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
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    int? MaxCapacity,
    bool IsActive
);
