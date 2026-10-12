using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Memberships.Dtos;

public record InitialMembershipPaymentRequest(
    decimal Amount,
    PaymentMethod Method,
    string? ExternalTransactionId = null,
    string? Notes = null,
    PaymentStatus Status = PaymentStatus.Completed
);

public record CreateMembershipRequest(
    Guid StudentId,
    Guid MembershipPlanId,
    DateOnly? StartDate = null,
    string? PromotionCode = null,
    InitialMembershipPaymentRequest? InitialPayment = null,
    Guid? PaymentId = null,
    bool ActivateImmediately = false,
    string? ActivationReason = null,
    string? Notes = null,
    string? IdempotencyKey = null
);

public record RenewMembershipRequest(
    Guid? MembershipPlanId = null,
    DateOnly? StartDate = null,
    string? PromotionCode = null,
    InitialMembershipPaymentRequest? InitialPayment = null,
    Guid? PaymentId = null,
    bool ActivateImmediately = false,
    string? ActivationReason = null,
    string? Notes = null,
    string? IdempotencyKey = null
);

public record ActivateMembershipRequest(
    string? Reason = null,
    string? Notes = null
);

public record SuspendMembershipRequest(
    string Reason,
    DateOnly? EndDate = null,
    bool ExtendExpirationDate = false,
    string? Notes = null,
    uint? Version = null
);

public record ReactivateMembershipRequest(
    string? Reason = null,
    string? Notes = null,
    uint? Version = null
);

public record CancelMembershipRequest(
    string Reason,
    string? Notes = null,
    uint? Version = null
);

public record UpdateMembershipNotesRequest(
    string? Notes,
    uint? Version = null
);

public record AssignMembershipRequest(
    Guid StudentId,
    Guid MembershipPlanId,
    DateOnly? StartDate = null
);

public record MembershipListFilter(
    Guid? StudentId = null,
    Guid? MembershipPlanId = null,
    MembershipStatus? Status = null,
    DateOnly? StartDateFrom = null,
    DateOnly? StartDateTo = null,
    DateOnly? EndDateFrom = null,
    DateOnly? EndDateTo = null,
    DateOnly? PurchasedFrom = null,
    DateOnly? PurchasedTo = null,
    int? ExpiringWithinDays = null,
    bool? HasAvailableCredits = null,
    string? SortBy = null,
    string? SortDir = null,
    int Page = 1,
    int PageSize = 20
);

public record MembershipStatusChangeDto(
    Guid Id,
    MembershipStatus? PreviousStatus,
    MembershipStatus NewStatus,
    string Reason,
    string? Notes,
    Guid? ChangedByUserId,
    DateTime ChangedAtUtc
);

public record MembershipSuspensionDto(
    Guid Id,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateOnly? ReactivatedAtDate,
    bool ExtendExpirationDate,
    int ExtensionDaysApplied,
    string Reason,
    string? Notes,
    Guid SuspendedByUserId,
    DateTime SuspendedAtUtc,
    Guid? ReactivatedByUserId,
    DateTime? ReactivatedAtUtc,
    bool IsActiveSuspension
);

public record MembershipSummaryDto(
    Guid Id,
    Guid StudentId,
    string StudentName,
    string StudentDni,
    Guid MembershipPlanId,
    string MembershipPlanName,
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalCredits,
    int AvailableCredits,
    MembershipStatus Status,
    bool IsValidToday,
    DocumentType? DocumentType = null,
    string? DocumentNumber = null,
    Guid? RenewedFromMembershipId = null,
    MembershipStatus EffectiveStatus = MembershipStatus.Active,
    DateTime PurchasedAtUtc = default,
    DateTime? ActivatedAtUtc = null,
    int DurationDaysSnapshot = 0,
    int InitialCredits = 0,
    int ExtraCreditsGranted = 0,
    bool IsUnlimited = false,
    bool AppliesToAllActivities = true,
    bool InitialCreditsLoaded = false,
    bool HasAvailableCredits = false,
    bool IsDepleted = false,
    bool StartsInFuture = false,
    int DaysRemaining = 0,
    decimal OriginalPrice = 0m,
    decimal DiscountAmount = 0m,
    decimal PurchasePrice = 0m,
    string Currency = "ARS",
    decimal TotalPaidAmount = 0m,
    decimal PendingBalance = 0m,
    bool HasOutstandingBalance = false,
    Guid? PromotionId = null,
    string? PromotionCode = null,
    string? Notes = null,
    uint Version = 0
);

public record MembershipListItemDto(
    Guid Id,
    Guid StudentId,
    string StudentName,
    string StudentDni,
    Guid MembershipPlanId,
    string MembershipPlanName,
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalCredits,
    int AvailableCredits,
    MembershipStatus Status,
    bool IsValidToday,
    DocumentType? DocumentType = null,
    string? DocumentNumber = null,
    Guid? RenewedFromMembershipId = null,
    MembershipStatus EffectiveStatus = MembershipStatus.Active,
    DateTime PurchasedAtUtc = default,
    DateTime? ActivatedAtUtc = null,
    int DurationDaysSnapshot = 0,
    int InitialCredits = 0,
    int ExtraCreditsGranted = 0,
    bool IsUnlimited = false,
    bool AppliesToAllActivities = true,
    bool InitialCreditsLoaded = false,
    bool HasAvailableCredits = false,
    bool IsDepleted = false,
    bool StartsInFuture = false,
    int DaysRemaining = 0,
    decimal OriginalPrice = 0m,
    decimal DiscountAmount = 0m,
    decimal PurchasePrice = 0m,
    string Currency = "ARS",
    decimal TotalPaidAmount = 0m,
    decimal PendingBalance = 0m,
    bool HasOutstandingBalance = false,
    Guid? PromotionId = null,
    string? PromotionCode = null,
    string? Notes = null,
    uint Version = 0
);

public record MembershipDetailDto(
    Guid Id,
    Guid StudentId,
    string StudentName,
    string StudentDni,
    Guid MembershipPlanId,
    string MembershipPlanName,
    DateOnly StartDate,
    DateOnly EndDate,
    int TotalCredits,
    int AvailableCredits,
    MembershipStatus Status,
    bool IsValidToday,
    DocumentType? DocumentType,
    string? DocumentNumber,
    Guid? RenewedFromMembershipId,
    MembershipStatus EffectiveStatus,
    DateTime PurchasedAtUtc,
    DateTime? ActivatedAtUtc,
    int DurationDaysSnapshot,
    int InitialCredits,
    int ExtraCreditsGranted,
    bool IsUnlimited,
    bool AppliesToAllActivities,
    bool InitialCreditsLoaded,
    bool HasAvailableCredits,
    bool IsDepleted,
    bool StartsInFuture,
    int DaysRemaining,
    decimal OriginalPrice,
    decimal DiscountAmount,
    decimal PurchasePrice,
    string Currency,
    decimal TotalPaidAmount,
    decimal PendingBalance,
    bool HasOutstandingBalance,
    Guid? PromotionId,
    string? PromotionCode,
    string? PromotionName,
    Guid? PromotionRedemptionId,
    Guid? CreatedByUserId,
    DateTime? CancelledAtUtc,
    Guid? CancelledByUserId,
    string? CancellationReason,
    string? Notes,
    uint Version,
    IReadOnlyList<Guid> AllowedActivityIds,
    IReadOnlyList<PaymentDto> Payments,
    IReadOnlyList<MembershipSuspensionDto> Suspensions,
    IReadOnlyList<MembershipStatusChangeDto> StatusChanges,
    MembershipCreditsSummaryDto? CreditsSummary = null
);

public record MembershipExpirationBatchResultDto(
    int ReactivatedCount,
    int ExpiredCount,
    int AutoCancelledCount,
    int WarningsSentCount
);
