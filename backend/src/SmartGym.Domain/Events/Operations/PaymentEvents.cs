using SmartGym.Domain.Common;

namespace SmartGym.Domain.Events.Operations;

public record PaymentCompletedEvent(
    Guid PaymentId,
    Guid PayerPersonId,
    Guid? MembershipId,
    Guid? ClassSessionId,
    decimal Amount,
    Guid? RegisteredByUserId,
    DateTime CompletedAtUtc
) : IDomainEvent;
