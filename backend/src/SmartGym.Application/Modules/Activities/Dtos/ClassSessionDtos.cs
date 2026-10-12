using System;
using System.Collections.Generic;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Activities.Dtos;

public record ClassSessionListItemDto(
    Guid Id,
    Guid? RecurringScheduleId,
    Guid ActivityId,
    string ActivityName,
    Guid RoomId,
    string RoomName,
    Guid ScheduledInstructorId,
    string ScheduledInstructorName,
    Guid EffectiveInstructorId,
    string EffectiveInstructorName,
    Guid? ScheduledSecretaryId,
    string? ScheduledSecretaryName,
    Guid? EffectiveSecretaryId,
    string? EffectiveSecretaryName,
    DateTime StartsAtUtc,
    DateTimeOffset StartsAtLocal,
    DateTimeOffset EndsAtLocal,
    int Capacity,
    int ReservedCount,
    int AttendedCount,
    int AvailableSpots,
    ClassSessionStatus Status,
    ClassSessionDisplayStatus DisplayStatus,
    bool IsDetachedFromSchedule,
    uint Version
);

public record ClassSessionDetailDto(
    Guid Id,
    Guid? RecurringScheduleId,
    Guid ActivityId,
    string ActivityName,
    Guid RoomId,
    string RoomName,
    Guid ScheduledInstructorId,
    string ScheduledInstructorName,
    Guid EffectiveInstructorId,
    string EffectiveInstructorName,
    Guid? ScheduledSecretaryId,
    string? ScheduledSecretaryName,
    Guid? EffectiveSecretaryId,
    string? EffectiveSecretaryName,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    DateTimeOffset StartsAtLocal,
    DateTimeOffset EndsAtLocal,
    int Capacity,
    int ReservedCount,
    int AttendedCount,
    int AvailableSpots,
    ClassSessionStatus Status,
    ClassSessionDisplayStatus DisplayStatus,
    bool IsDetachedFromSchedule,
    string? Notes,
    DateTime? SuspendedAtUtc,
    Guid? SuspendedByUserId,
    ClassSessionSuspensionReason? SuspensionReason,
    string? SuspensionObservations,
    DateTime? CancelledAtUtc,
    Guid? CancelledByUserId,
    ClassSessionCancellationReason? CancellationReason,
    string? CancellationObservations,
    Guid? RescheduledFromSessionId,
    Guid? RescheduledToSessionId,
    List<ClassSessionStaffChangeDto> StaffChanges,
    uint Version
);

public record ClassSessionStaffChangeDto(
    Guid Id,
    string Role,
    Guid? PreviousPersonId,
    string? PreviousPersonName,
    Guid? NewPersonId,
    string? NewPersonName,
    string Reason,
    Guid? ChangedByUserId,
    DateTime ChangedAtUtc
);

public record ClassSessionAvailabilityDto(
    Guid Id,
    Guid ActivityId,
    string ActivityName,
    Guid RoomId,
    string RoomName,
    string EffectiveInstructorName,
    DateTime StartsAtUtc,
    DateTimeOffset StartsAtLocal,
    DateTimeOffset EndsAtLocal,
    int Capacity,
    int ReservedCount,
    int AvailableSpots,
    bool IsFull
);

public record CreateClassSessionRequest(
    Guid ActivityId,
    Guid RoomId,
    Guid InstructorId,
    Guid? SecretaryId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int? Capacity,
    string? Notes
);

public record UpdateClassSessionRequest(
    int Capacity,
    string? Notes,
    uint Version
);

public record SuspendClassSessionRequest(
    ClassSessionSuspensionReason Reason,
    string? Observations
);

public record CancelClassSessionRequest(
    ClassSessionCancellationReason Reason,
    string? Observations
);

public record RescheduleClassSessionRequest(
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    Guid? RoomId,
    string Reason
);

public record ReplaceStaffRequest(
    Guid? PersonId,
    string Reason,
    uint Version
);

public record ChangeRoomRequest(
    Guid RoomId,
    uint Version
);

public record GenerateSessionsRequest(
    DateOnly From,
    DateOnly To,
    Guid? RecurringScheduleId
);

public record GenerationResultDto(
    int Created,
    int Skipped,
    List<GenerationConflictDto> Conflicts
);

public record GenerationConflictDto(
    Guid ScheduleId,
    DateOnly Date,
    string ConflictType // "ROOM_OVERLAP" | "INSTRUCTOR_OVERLAP" | "NO_CAPACITY"
);
