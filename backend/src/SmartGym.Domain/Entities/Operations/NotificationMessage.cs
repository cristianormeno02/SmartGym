using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Operations;

public class NotificationMessage : BaseEntity
{
    public Guid RecipientPersonId { get; set; }
    public Person RecipientPerson { get; set; } = null!;

    public NotificationCategory Category { get; set; } = NotificationCategory.Reservations;
    public NotificationType Type { get; set; }
    public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public string? ActionUrl { get; set; }

    public bool IsRead { get; set; } = false;
    public DateTime? ReadAtUtc { get; set; }
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; set; }

    public string? MetadataJson { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? CorrelationId { get; set; }

    public ICollection<NotificationDelivery> Deliveries { get; set; } = new List<NotificationDelivery>();

    public static NotificationMessage Create(
        Guid recipientPersonId,
        NotificationType type,
        string title,
        string body,
        DateTime nowUtc,
        string? relatedEntityType = null,
        Guid? relatedEntityId = null,
        string? actionUrl = null,
        string? idempotencyKey = null,
        string? metadataJson = null,
        string? correlationId = null,
        DateTime? expiresAtUtc = null)
    {
        if (recipientPersonId == Guid.Empty)
        {
            throw new ArgumentException("El destinatario de la notificación es obligatorio.", nameof(recipientPersonId));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("El título de la notificación es obligatorio.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("El cuerpo de la notificación es obligatorio.", nameof(body));
        }

        var normalizedActionUrl = ValidateAndNormalizeActionUrl(actionUrl ?? type.GetDefaultActionUrl());

        return new NotificationMessage
        {
            Id = Guid.NewGuid(),
            RecipientPersonId = recipientPersonId,
            Category = type.GetCategory(),
            Type = type,
            Channel = NotificationChannel.InApp,
            Title = title.Trim(),
            Body = body.Trim(),
            RelatedEntityType = string.IsNullOrWhiteSpace(relatedEntityType) ? type.GetDefaultRelatedEntityType() : relatedEntityType.Trim(),
            RelatedEntityId = relatedEntityId,
            ActionUrl = normalizedActionUrl,
            IsRead = false,
            ReadAtUtc = null,
            SentAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            ExpiresAtUtc = expiresAtUtc,
            MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson.Trim(),
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim(),
            IsActive = true
        };
    }

    public static string? ValidateAndNormalizeActionUrl(string? actionUrl)
    {
        if (string.IsNullOrWhiteSpace(actionUrl))
        {
            return null;
        }

        var trimmed = actionUrl.Trim();
        if (!trimmed.StartsWith('/') ||
            trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.StartsWith("/\\", StringComparison.Ordinal) ||
            trimmed.Contains("://", StringComparison.Ordinal) ||
            trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("La URL de acción debe ser una ruta interna relativa válida que comience con '/'.", nameof(actionUrl));
        }

        return trimmed;
    }

    public static bool IsValidInternalActionUrl(string? actionUrl)
    {
        try
        {
            _ = ValidateAndNormalizeActionUrl(actionUrl);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public void MarkAsRead(DateTime nowUtc)
    {
        IsRead = true;
        ReadAtUtc ??= nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkAsUnread(DateTime nowUtc)
    {
        IsRead = false;
        ReadAtUtc = null;
        UpdatedAtUtc = nowUtc;
    }
}
