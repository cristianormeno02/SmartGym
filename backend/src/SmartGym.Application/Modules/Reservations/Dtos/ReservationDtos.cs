using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Reservations.Dtos;

public static class ReservationErrorCodes
{
    public const string ClassNotOpen = "CLASS_NOT_OPEN";
    public const string ClassAlreadyStarted = "CLASS_ALREADY_STARTED";
    public const string BookingWindowClosed = "BOOKING_WINDOW_CLOSED";
    public const string AlreadyReserved = "ALREADY_RESERVED";
    public const string NoAccessRight = "NO_ACCESS_RIGHT";
    public const string ClassFull = "CLASS_FULL";
    public const string WaitlistFull = "WAITLIST_FULL";
    public const string WaitlistOfferExpired = "WAITLIST_OFFER_EXPIRED";
    public const string PaymentNotConfirmed = "PAYMENT_NOT_CONFIRMED";
    public const string PromotionNotApplicable = "PROMOTION_NOT_APPLICABLE";
    public const string AccessRightInUse = "ACCESS_RIGHT_IN_USE";
    public const string IdempotencyKeyMismatch = "IDEMPOTENCY_KEY_MISMATCH";
    public const string InvalidStateTransition = "INVALID_STATE_TRANSITION";
    public const string AttendanceWindowClosed = "ATTENDANCE_WINDOW_CLOSED";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
}

public record ReservationStatusChangeDto(
    Guid Id,
    Guid ReservationId,
    ReservationStatus? PreviousStatus,
    ReservationStatus NewStatus,
    string Reason,
    Guid? ChangedByUserId,
    ReservationChangeTrigger Trigger,
    DateTime ChangedAtUtc
);

public record ReservationDto(
    Guid Id,
    Guid ClassSessionId,
    string ActivityName,
    DateOnly ClassDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    string RoomName,
    Guid StudentId,
    string StudentName,
    string StudentDni,
    Guid? MembershipId,
    ReservationStatus Status,
    DateTime ReservedAtUtc,
    DateTime? ConfirmedAtUtc,
    DateTime? CancelledAtUtc,
    string? CancellationReason,
    bool IsLateCancellation,
    DateTime? AttendedAtUtc,
    AttendanceSource? AttendanceSource,
    int? WaitListPosition,
    DocumentType? DocumentType = null,
    string? DocumentNumber = null,
    ReservationOrigin Origin = ReservationOrigin.StudentSelfService,
    ReservationAccessType AccessType = ReservationAccessType.MembershipCredit,
    Guid? CreditMovementId = null,
    Guid? RefundCreditMovementId = null,
    Guid? SingleClassTicketMovementId = null,
    Guid? PaymentId = null,
    Guid? PromotionRedemptionId = null,
    string? RequestedPromotionCode = null,
    Guid? CreatedByUserId = null,
    DateTime? WaitlistEnteredAtUtc = null,
    DateTime? WaitlistPromotedAtUtc = null,
    DateTime? WaitlistOfferExpiresAtUtc = null,
    Guid? CancelledByUserId = null,
    ReservationCancellationSource? CancellationSource = null,
    bool IsPolicyOverride = false,
    string? PolicyOverrideReason = null,
    bool AffectedBySessionChange = false,
    Guid? RescheduledToSessionId = null,
    Guid? CheckedInByUserId = null,
    string? Notes = null,
    string? IdempotencyKey = null,
    bool WasIdempotentReplay = false,
    IReadOnlyList<ReservationStatusChangeDto>? StatusHistory = null,
    Guid? ActivityId = null,
    DateTime? StartsAtUtc = null,
    DateTime? EndsAtUtc = null
);

public record ReservationDetailDto(
    Guid Id,
    Guid ClassSessionId,
    Guid ActivityId,
    string ActivityName,
    DateOnly ClassDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string RoomName,
    Guid StudentId,
    string StudentName,
    string StudentDni,
    DocumentType? DocumentType,
    string? DocumentNumber,
    ReservationStatus Status,
    ReservationOrigin Origin,
    ReservationAccessType AccessType,
    Guid? MembershipId,
    Guid? CreditMovementId,
    Guid? RefundCreditMovementId,
    Guid? SingleClassTicketMovementId,
    Guid? PaymentId,
    Guid? PromotionRedemptionId,
    string? RequestedPromotionCode,
    DateTime ReservedAtUtc,
    DateTime? ConfirmedAtUtc,
    Guid? CreatedByUserId,
    int? WaitListPosition,
    DateTime? WaitlistEnteredAtUtc,
    DateTime? WaitlistPromotedAtUtc,
    DateTime? WaitlistOfferExpiresAtUtc,
    DateTime? CancelledAtUtc,
    Guid? CancelledByUserId,
    string? CancellationReason,
    ReservationCancellationSource? CancellationSource,
    bool IsLateCancellation,
    bool IsPolicyOverride,
    string? PolicyOverrideReason,
    bool AffectedBySessionChange,
    Guid? RescheduledToSessionId,
    DateTime? AttendedAtUtc,
    Guid? CheckedInByUserId,
    AttendanceSource? AttendanceSource,
    string? Notes,
    string? IdempotencyKey,
    IReadOnlyList<ReservationStatusChangeDto> StatusHistory
);

public record CreateReservationRequest(
    Guid ClassSessionId,
    Guid? StudentId = null,
    Guid? MembershipId = null,
    Guid? SingleClassTicketMovementId = null,
    string? PromotionCode = null,
    bool AllowWaitlist = true,
    string? Notes = null,
    bool OverrideBookingWindow = false,
    string? PolicyOverrideReason = null,
    string? IdempotencyKey = null
);

public record CancelReservationRequest(
    string? Reason = null,
    bool OverridePolicy = false,
    string? PolicyOverrideReason = null
);

public record JoinWaitlistRequest(
    Guid? StudentId = null,
    Guid? MembershipId = null,
    Guid? SingleClassTicketMovementId = null,
    string? PromotionCode = null,
    string? Notes = null,
    string? IdempotencyKey = null
);

public record RecordAttendanceRequest(
    Guid ReservationId,
    AttendanceSource Source
);

public record SessionAttendanceSummaryDto(
    Guid ClassSessionId,
    string ActivityName,
    DateOnly Date,
    int TotalCapacity,
    int ReservedCount,
    int AttendedCount,
    List<ReservationDto> Attendees
);

public record WaitlistEntryDto(
    Guid ReservationId,
    Guid ClassSessionId,
    Guid StudentId,
    string StudentName,
    string StudentDni,
    ReservationStatus Status,
    int? WaitListPosition,
    DateTime? WaitlistEnteredAtUtc,
    DateTime? WaitlistOfferExpiresAtUtc,
    ReservationAccessType AccessType,
    Guid? MembershipId
);

public record ReservableClassSessionDto(
    Guid ClassSessionId,
    Guid ActivityId,
    string ActivityName,
    Guid RoomId,
    string RoomName,
    Guid EffectiveInstructorId,
    string InstructorName,
    DateOnly ClassDate,
    TimeOnly StartTime,
    TimeOnly EndTime,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    DateTimeOffset StartsAtLocal,
    DateTimeOffset EndsAtLocal,
    int Capacity,
    int ReservedCount,
    int AvailableSpots,
    int WaitlistCount,
    bool IsWithinBookingWindow,
    bool HasActiveReservation,
    Guid? ActiveReservationId,
    ReservationStatus? ActiveReservationStatus,
    int? ActiveWaitlistPosition,
    bool CanReserve,
    bool CanJoinWaitlist,
    ReservationAccessType? SuggestedAccessType,
    Guid? SelectedMembershipId,
    int? UsableCredits,
    IReadOnlyList<EligibilityIssueDto> IneligibilityReasons
);

public record ReservationFilterRequest(
    Guid? StudentId = null,
    Guid? ClassSessionId = null,
    Guid? ActivityId = null,
    ReservationStatus? Status = null,
    ReservationAccessType? AccessType = null,
    ReservationOrigin? Origin = null,
    DateOnly? From = null,
    DateOnly? To = null,
    bool? IsLateCancellation = null,
    bool? AffectedBySessionChange = null,
    int Page = 1,
    int PageSize = 20
);

public record StudentReservationHistoryFilterRequest(
    ReservationStatus? Status = null,
    DateOnly? From = null,
    DateOnly? To = null
);

public record AvailableClassesFilterRequest(
    DateOnly From,
    DateOnly To,
    Guid? StudentId = null,
    Guid? ActivityId = null,
    Guid? RoomId = null,
    Guid? InstructorId = null
);

public record WaitlistProcessResultDto(
    int ExpiredOffersCancelled,
    int StartedSessionsWaitlistExpired,
    int CandidatesPromoted
);
