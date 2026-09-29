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
    string? Notes
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
    DateTime SentAtUtc
);

public record SendNotificationRequest(
    Guid RecipientPersonId,
    NotificationType Type,
    NotificationChannel Channel,
    string Title,
    string Body
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
