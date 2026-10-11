using SmartGym.Domain.Common;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Events.Reservations;

public record ReservationConfirmedEvent(
    Guid ReservationId,
    Guid ClassSessionId,
    Guid StudentId,
    ReservationAccessType AccessType,
    DateTime ConfirmedAtUtc
) : IPostCommitDomainEvent;

public record ReservationWaitlistedEvent(
    Guid ReservationId,
    Guid ClassSessionId,
    Guid StudentId,
    int WaitListPosition,
    DateTime WaitlistEnteredAtUtc
) : IPostCommitDomainEvent;

public record ReservationCancelledEvent(
    Guid ReservationId,
    Guid ClassSessionId,
    Guid StudentId,
    ReservationStatus PreviousStatus,
    ReservationCancellationSource CancellationSource,
    string Reason,
    bool IsLateCancellation,
    DateTime CancelledAtUtc
) : IPostCommitDomainEvent;

public record ReservationPromotedFromWaitlistEvent(
    Guid ReservationId,
    Guid ClassSessionId,
    Guid StudentId,
    ReservationAccessType AccessType,
    DateTime PromotedAtUtc
) : IPostCommitDomainEvent;

public record WaitlistOfferCreatedEvent(
    Guid ReservationId,
    Guid ClassSessionId,
    Guid StudentId,
    DateTime OfferExpiresAtUtc,
    DateTime OfferedAtUtc
) : IPostCommitDomainEvent;

public record WaitlistOfferExpiredEvent(
    Guid ReservationId,
    Guid ClassSessionId,
    Guid StudentId,
    DateTime ExpiredAtUtc
) : IPostCommitDomainEvent;
