using SmartGym.Domain.Common;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Operations;

/// <summary>
/// Notificación verificada de un proveedor de pagos. Sólo se registran eventos cuya firma ya se comprobó,
/// y no se guarda el cuerpo de la notificación: el estado real se consulta al proveedor.
/// </summary>
public class PaymentWebhookEvent : BaseEntity
{
    public PaymentProvider Provider { get; set; }
    public string ExternalEventId { get; set; } = string.Empty;
    public string? ExternalTransactionId { get; set; }
    public string? EventType { get; set; }
    public PaymentWebhookEventStatus Status { get; set; } = PaymentWebhookEventStatus.Received;

    public Guid? PaymentId { get; set; }
    public Payment? Payment { get; set; }

    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
    public string? Detail { get; set; }
}
