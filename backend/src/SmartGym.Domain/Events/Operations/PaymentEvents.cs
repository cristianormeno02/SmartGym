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

/// <summary>
/// Un pago que ya estaba confirmado se aplicó a una membresía. Es pre-commit, como
/// <see cref="PaymentCompletedEvent"/>, para que la activación sea atómica con la aplicación.
/// </summary>
public record PaymentAppliedEvent(
    Guid PaymentId,
    Guid PayerPersonId,
    Guid MembershipId,
    decimal Amount,
    Guid? AppliedByUserId,
    DateTime AppliedAtUtc
) : IDomainEvent;

public record PaymentConfirmedNotificationEvent(
    Guid PaymentId,
    Guid PayerPersonId,
    decimal Amount,
    string Currency
) : IPostCommitDomainEvent;

public record PaymentFailedNotificationEvent(
    Guid PaymentId,
    Guid PayerPersonId,
    decimal Amount,
    string Currency,
    string Reason
) : IPostCommitDomainEvent;

public record PaymentRefundedNotificationEvent(
    Guid PaymentId,
    Guid RefundId,
    Guid PayerPersonId,
    decimal RefundAmount,
    string Currency
) : IPostCommitDomainEvent;
