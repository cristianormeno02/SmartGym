using System.Text.RegularExpressions;
using SmartGym.Domain.Common;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Operations;

public class NotificationDelivery : BaseEntity
{
    private static readonly Regex SensitivePattern = new(
        @"(password|passwd|pwd|secret|token|apikey|api_key|authorization|bearer)\s*[:=]\s*[^\s,;]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Guid NotificationId { get; set; }
    public NotificationMessage Notification { get; set; } = null!;

    public NotificationChannel Channel { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? RecipientAddress { get; set; }

    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Pending;
    public int AttemptCount { get; set; } = 0;
    public int MaxAttempts { get; set; } = 3;

    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }

    public string? ProviderMessageId { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }

    public Guid? RetriedByUserId { get; set; }
    public string? RetryReason { get; set; }

    public static NotificationDelivery CreatePending(
        Guid notificationId,
        NotificationChannel channel,
        string provider,
        string? recipientAddress,
        DateTime nowUtc,
        int maxAttempts = 3)
    {
        if (notificationId == Guid.Empty)
        {
            throw new ArgumentException("El identificador de notificación es obligatorio.", nameof(notificationId));
        }

        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "El número máximo de intentos debe ser al menos 1.");
        }

        return new NotificationDelivery
        {
            Id = Guid.NewGuid(),
            NotificationId = notificationId,
            Channel = channel,
            Provider = string.IsNullOrWhiteSpace(provider) ? "Unconfigured" : provider.Trim(),
            RecipientAddress = string.IsNullOrWhiteSpace(recipientAddress) ? null : recipientAddress.Trim(),
            Status = NotificationDeliveryStatus.Pending,
            AttemptCount = 0,
            MaxAttempts = maxAttempts,
            NextAttemptAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            IsActive = true
        };
    }

    public void MarkProcessing(DateTime nowUtc)
    {
        Status = NotificationDeliveryStatus.Processing;
        LastAttemptAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkSent(string? providerMessageId, DateTime nowUtc)
    {
        AttemptCount++;
        LastAttemptAtUtc = nowUtc;
        SentAtUtc = nowUtc;
        NextAttemptAtUtc = null;
        Status = NotificationDeliveryStatus.Sent;
        ProviderMessageId = string.IsNullOrWhiteSpace(providerMessageId) ? null : providerMessageId.Trim();
        LastErrorCode = null;
        LastErrorMessage = null;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkDelivered(string? providerMessageId, DateTime nowUtc)
    {
        if (AttemptCount == 0)
        {
            AttemptCount = 1;
        }

        LastAttemptAtUtc ??= nowUtc;
        SentAtUtc ??= nowUtc;
        DeliveredAtUtc = nowUtc;
        NextAttemptAtUtc = null;
        Status = NotificationDeliveryStatus.Delivered;
        if (!string.IsNullOrWhiteSpace(providerMessageId))
        {
            ProviderMessageId = providerMessageId.Trim();
        }

        LastErrorCode = null;
        LastErrorMessage = null;
        UpdatedAtUtc = nowUtc;
    }

    public void RecordTransientFailure(
        string errorCode,
        string errorMessage,
        DateTime nowUtc,
        int baseDelaySeconds = 30,
        int maxDelaySeconds = 3600,
        int jitterSeconds = 0)
    {
        AttemptCount++;
        LastAttemptAtUtc = nowUtc;
        LastErrorCode = SanitizeCode(errorCode, "TRANSIENT_DELIVERY_ERROR");
        LastErrorMessage = SanitizeErrorMessage(errorMessage);
        UpdatedAtUtc = nowUtc;

        if (AttemptCount >= MaxAttempts)
        {
            Status = NotificationDeliveryStatus.Failed;
            NextAttemptAtUtc = null;
            return;
        }

        Status = NotificationDeliveryStatus.Pending;
        var exponent = Math.Max(0, AttemptCount - 1);
        var exponentialDelay = Math.Min(
            Math.Max(1, maxDelaySeconds),
            Math.Max(1, baseDelaySeconds) * (int)Math.Pow(2, Math.Min(10, exponent)));
        var totalDelaySeconds = exponentialDelay + Math.Max(0, jitterSeconds);
        NextAttemptAtUtc = nowUtc.AddSeconds(totalDelaySeconds);
    }

    public void RecordPermanentFailure(string errorCode, string errorMessage, DateTime nowUtc)
    {
        AttemptCount++;
        LastAttemptAtUtc = nowUtc;
        NextAttemptAtUtc = null;
        Status = NotificationDeliveryStatus.Failed;
        LastErrorCode = SanitizeCode(errorCode, "PERMANENT_DELIVERY_ERROR");
        LastErrorMessage = SanitizeErrorMessage(errorMessage);
        UpdatedAtUtc = nowUtc;
    }

    public void MarkSkipped(string reasonCode, string reasonMessage, DateTime nowUtc)
    {
        LastAttemptAtUtc = nowUtc;
        NextAttemptAtUtc = null;
        Status = NotificationDeliveryStatus.Skipped;
        LastErrorCode = SanitizeCode(reasonCode, "DELIVERY_SKIPPED");
        LastErrorMessage = SanitizeErrorMessage(reasonMessage);
        UpdatedAtUtc = nowUtc;
    }

    public void MarkCancelled(string reason, DateTime nowUtc)
    {
        NextAttemptAtUtc = null;
        Status = NotificationDeliveryStatus.Cancelled;
        LastErrorCode = "DELIVERY_CANCELLED";
        LastErrorMessage = SanitizeErrorMessage(reason);
        UpdatedAtUtc = nowUtc;
    }

    public void RequestManualRetry(Guid userId, string? reason, DateTime nowUtc)
    {
        if (Status != NotificationDeliveryStatus.Failed && Status != NotificationDeliveryStatus.Skipped)
        {
            throw new InvalidOperationException(
                $"Sólo se puede reintentar manualmente una entrega en estado Failed o Skipped (estado actual: {Status}).");
        }

        Status = NotificationDeliveryStatus.Pending;
        AttemptCount = 0;
        NextAttemptAtUtc = nowUtc;
        RetriedByUserId = userId;
        RetryReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAtUtc = nowUtc;
    }

    public static string SanitizeErrorMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Error de entrega sin detalle adicional.";
        }

        var redacted = SensitivePattern.Replace(message.Trim(), "$1=[REDACTED]");
        return redacted.Length <= 500 ? redacted : redacted[..500];
    }

    private static string SanitizeCode(string? code, string fallback)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return fallback;
        }

        var trimmed = code.Trim();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }
}
