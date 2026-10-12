using System;
using SmartGym.Domain.Common;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Events.Activities;

public record ClassSessionSuspended(
    Guid SessionId,
    ClassSessionSuspensionReason Reason,
    string? Observations,
    Guid? ByUserId,
    DateTime AtUtc
) : IDomainEvent;

public record ClassSessionCancelled(
    Guid SessionId,
    ClassSessionCancellationReason Reason,
    string? Observations,
    Guid? ByUserId,
    DateTime AtUtc
) : IDomainEvent;

public record ClassSessionRescheduled(
    Guid OriginalSessionId,
    Guid NewSessionId,
    DateTime NewStartsAtUtc,
    DateTime NewEndsAtUtc,
    Guid? ByUserId
) : IDomainEvent;

public record ClassSessionRoomChanged(
    Guid SessionId,
    Guid PreviousRoomId,
    Guid NewRoomId,
    Guid? ByUserId,
    DateTime AtUtc
) : IDomainEvent;

public record ClassSessionCapacityIncreased(
    Guid SessionId,
    int PreviousCapacity,
    int NewCapacity,
    Guid? ByUserId,
    DateTime AtUtc
) : IDomainEvent;

