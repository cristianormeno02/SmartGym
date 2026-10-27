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
    public const string BookingNotRequired = "BOOKING_NOT_REQUIRED";
    public const string DropInWindowClosed = "DROP_IN_WINDOW_CLOSED";
    public const string AlreadyCheckedIn = "ALREADY_CHECKED_IN";
    public const string SingleClassPlanNotAvailable = "SINGLE_CLASS_PLAN_NOT_AVAILABLE";
    public const string AttendanceRevocationWindowClosed = "ATTENDANCE_REVOCATION_WINDOW_CLOSED";
    public const string PaymentFailed = "PAYMENT_FAILED";
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
    DateTime? EndsAtUtc = null,
    bool IsDropIn = false,
    DateTime? AttendanceRevokedAtUtc = null,
    Guid? AttendanceRevokedByUserId = null,
    AttendanceRevocationReason? AttendanceRevocationReason = null,
    string? AttendanceRevocationNotes = null
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
    IReadOnlyList<ReservationStatusChangeDto> StatusHistory,
    bool IsDropIn = false,
    DateTime? AttendanceRevokedAtUtc = null,
    Guid? AttendanceRevokedByUserId = null,
    AttendanceRevocationReason? AttendanceRevocationReason = null,
    string? AttendanceRevocationNotes = null
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
    IReadOnlyList<EligibilityIssueDto> IneligibilityReasons,
    bool RequiresBooking = true
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

public record DeskEligibleMembershipDto(
    Guid MembershipId,
    string PlanName,
    bool IsUnlimited,
    int UsableCredits,
    DateOnly ValidUntil
);

public record SingleClassPassOptionDto(
    Guid PlanId,
    string PlanName,
    decimal Price
);

public record DeskEligibilityDto(
    Guid ClassSessionId,
    Guid StudentId,
    string StudentFullName,
    string StudentDni,
    bool RequiresBooking,
    bool IsWithinDropInWindow,
    int AvailableSpots,
    bool HasActiveReservation,
    ReservationStatus? ExistingReservationStatus,
    Guid? ExistingReservationId,
    bool CanCheckInWithMembership,
    bool CanPurchaseSingleClassPass,
    bool CanEnrollReservation,
    List<DeskEligibleMembershipDto> EligibleMemberships,
    List<SingleClassPassOptionDto> SingleClassPassOptions,
    List<EligibilityIssueDto> IneligibilityReasons,
    string? StudentPhotoUrl = null,
    PersonStatus StudentStatus = PersonStatus.Active,
    ReservationAccessType? SuggestedAccessType = null
);

public record DeskDropInRequest(
    Guid ClassSessionId,
    Guid StudentId,
    Guid? MembershipId = null,
    Guid? SingleClassPlanId = null,
    PaymentMethod? PaymentMethod = null,
    string? IdempotencyKey = null,
    string? Notes = null
);

public record DeskDropInResponse(
    ReservationDto Reservation,
    Guid? PaymentId,
    bool WasIdempotentReplay = false
);

public record RevokeAttendanceRequest(
    AttendanceRevocationReason Reason,
    string Notes
);

public record RevokeAttendanceResponse(
    ReservationDto Reservation,
    string ActionTaken,
    bool RightsRestored,
    Guid? RefundCreditMovementId,
    Guid? PaymentId
);
