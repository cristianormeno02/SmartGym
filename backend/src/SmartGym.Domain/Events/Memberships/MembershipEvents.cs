using SmartGym.Domain.Common;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Events.Memberships;

public record MembershipActivatedEvent(
    Guid MembershipId,
    Guid StudentId,
    Guid? ActivatedByUserId,
    DateTime ActivatedAtUtc
) : IDomainEvent;

public record MembershipCancelledEvent(
    Guid MembershipId,
    Guid StudentId,
    MembershipStatus PreviousStatus,
    string Reason,
    Guid? CancelledByUserId,
    DateTime CancelledAtUtc
) : IDomainEvent;

public record MembershipExpiredEvent(
    Guid MembershipId,
    Guid StudentId,
    DateTime ExpiredAtUtc
) : IDomainEvent;

public record MembershipSuspendedEvent(
    Guid MembershipId,
    Guid StudentId,
    Guid SuspensionId,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool ExtendExpirationDate,
    string Reason,
    Guid SuspendedByUserId,
    DateTime SuspendedAtUtc
) : IDomainEvent;
