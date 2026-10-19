namespace SmartGym.Application.Modules.Activities.Dtos;

public enum ScheduleConflictKind
{
    Room,
    Instructor,
    WithinBatch
}

public record ScheduleConflictDto(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    ScheduleConflictKind Kind,
    Guid? ConflictingScheduleId = null,
    string? ConflictingActivityName = null,
    TimeOnly? ConflictingStartTime = null,
    TimeOnly? ConflictingEndTime = null
);
