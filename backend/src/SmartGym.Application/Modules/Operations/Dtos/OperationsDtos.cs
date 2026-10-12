using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Dtos;

public record PaymentDto(
    Guid Id,
    Guid PayerPersonId,
    string PayerName,
    Guid? MembershipId,
    Guid? ClassSessionId,
    decimal Amount,
    PaymentMethod Method,
    PaymentStatus Status,
    string? ExternalTransactionId,
    string? ReceiptUrl,
    string? Notes,
    DateTime PaidAtUtc
);

public record RegisterPaymentRequest(
    Guid PayerPersonId,
    Guid? MembershipId,
    Guid? ClassSessionId,
    decimal Amount,
    PaymentMethod Method,
    string? ExternalTransactionId,
    string? Notes,
    PaymentStatus Status = PaymentStatus.Completed
);

public record ConfirmPaymentRequest(
    string? ExternalTransactionId = null,
    string? Notes = null
);

public record FailPaymentRequest(
    string Reason
);

public record MedicalCertificateStatusDto(
    Guid PersonId,
    string FullName,
    bool HasCertificate,
    string? CertificateUrl,
    DateTime? ExpirationDate,
    bool IsValid,
    bool IsUnderage,
    bool HasValidEmergencyContact,
    string? EmergencyContactName,
    string? EmergencyContactPhone
);

public record NotificationDto(
    Guid Id,
    Guid RecipientPersonId,
    string RecipientName,
    NotificationType Type,
    NotificationChannel Channel,
    string Title,
    string Body,
    bool IsRead,
    DateTime SentAtUtc,
    NotificationCategory Category = NotificationCategory.Reservations,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null,
    string? ActionUrl = null,
    DateTime? ReadAtUtc = null,
    DateTime? ExpiresAtUtc = null,
    string? MetadataJson = null,
    string? IdempotencyKey = null,
    string? CorrelationId = null
);

public record SendNotificationRequest(
    Guid RecipientPersonId,
    NotificationType Type,
    NotificationChannel Channel,
    string Title,
    string Body,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null,
    string? ActionUrl = null,
    string? IdempotencyKey = null,
    string? CorrelationId = null
);

public record NotificationQueryFilter(
    bool? IsRead = null,
    NotificationCategory? Category = null,
    NotificationType? Type = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Page = 1,
    int PageSize = 20
);

public record AdminNotificationQueryFilter(
    Guid? RecipientPersonId = null,
    NotificationCategory? Category = null,
    NotificationType? Type = null,
    bool? IsRead = null,
    NotificationDeliveryStatus? DeliveryStatus = null,
    NotificationChannel? Channel = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Page = 1,
    int PageSize = 20
);

public record UnreadNotificationsCountDto(int UnreadCount);

public record MarkAllNotificationsReadResultDto(int MarkedCount);

public record NotificationPreferenceItemDto(
    NotificationCategory Category,
    NotificationChannel Channel,
    bool IsEnabled,
    bool IsLocked,
    bool IsTransactional
);

public record UpdateNotificationPreferenceEntry(
    NotificationCategory Category,
    NotificationChannel Channel,
    bool IsEnabled
);

public record UpdateNotificationPreferencesRequest(
    IReadOnlyList<UpdateNotificationPreferenceEntry> Preferences
);

public record NotificationDeliveryDto(
    Guid Id,
    Guid NotificationMessageId,
    NotificationChannel Channel,
    NotificationDeliveryStatus Status,
    string? Destination,
    string? ProviderName,
    string? ProviderMessageId,
    int AttemptCount,
    int MaxAttempts,
    DateTime? LastAttemptAtUtc,
    DateTime? NextAttemptAtUtc,
    DateTime? DeliveredAtUtc,
    string? ErrorCode,
    string? ErrorMessage,
    bool IsPermanentFailure,
    Guid? RetriedByUserId,
    DateTime CreatedAtUtc
);

public record RetryNotificationDeliveryRequest(
    string? Reason = null
);

public record NotificationTemplateDto(
    Guid Id,
    string Code,
    NotificationType Type,
    NotificationCategory Category,
    NotificationChannel Channel,
    string Locale,
    int Version,
    NotificationTemplateStatus Status,
    string SubjectTemplate,
    string BodyTemplate,
    string? PlainTextTemplate,
    IReadOnlyList<string> AllowedVariables,
    Guid? CreatedByUserId,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc
);

public record CreateNotificationTemplateRequest(
    string Code,
    NotificationType Type,
    NotificationChannel Channel,
    string SubjectTemplate,
    string BodyTemplate,
    string? PlainTextTemplate = null,
    IReadOnlyList<string>? AllowedVariables = null,
    string Locale = "es-AR",
    int? Version = null,
    bool ActivateImmediately = false
);

public record UpdateNotificationTemplateRequest(
    string SubjectTemplate,
    string BodyTemplate,
    string? PlainTextTemplate = null,
    IReadOnlyList<string>? AllowedVariables = null
);

public record NotificationTemplateQueryFilter(
    NotificationType? Type = null,
    NotificationChannel? Channel = null,
    string? Locale = null,
    NotificationTemplateStatus? Status = null
);

public record RenderedNotificationContent(
    string Subject,
    string Body,
    string? PlainTextBody,
    string TemplateCode,
    int TemplateVersion,
    IReadOnlyList<string>? MissingVariables = null
);

public record AuditLogDto(
    Guid Id,
    Guid? UserId,
    string? UserEmail,
    string Action,
    string EntityName,
    string? EntityId,
    string? OldValues,
    string? NewValues,
    string? IpAddress,
    DateTime TimestampUtc
);

public record DashboardMetricsDto(
    int TotalActiveStudents,
    int TotalActiveInstructors,
    int TotalClassesToday,
    int TotalReservationsToday,
    int TotalAttendanceToday,
    double AttendanceRateTodayPercent,
    decimal MonthlyRevenue,
    int ExpiringMembershipsNext7Days,
    int PendingMedicalCertificatesCount
);
