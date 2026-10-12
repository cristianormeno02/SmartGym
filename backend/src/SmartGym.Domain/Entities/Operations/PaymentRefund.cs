using SmartGym.Domain.Common;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Operations;

public class PaymentRefund : BaseEntity
{
    public Guid PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ARS";
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }

    // Una devolución de un pago manual nace Completed; la de un pago externo queda Pending hasta que
    // el proveedor la acepta o la rechaza.
    public PaymentRefundStatus Status { get; set; } = PaymentRefundStatus.Completed;
    public string? ExternalRefundId { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? FailureReason { get; set; }

    // Nulo sólo en las devoluciones migradas desde el modelo anterior, que no registraba al responsable.
    public Guid? RequestedByUserId { get; set; }
    public string? IdempotencyKey { get; set; }

    public void Complete(string? externalRefundId = null, DateTime? nowUtc = null)
    {
        EnsurePending("completar");

        var now = nowUtc ?? DateTime.UtcNow;
        Status = PaymentRefundStatus.Completed;
        ExternalRefundId = string.IsNullOrWhiteSpace(externalRefundId) ? null : externalRefundId.Trim();
        CompletedAtUtc = now;
        UpdatedAtUtc = now;
    }

    public void Fail(string? reason, DateTime? nowUtc = null)
    {
        EnsurePending("rechazar");

        Status = PaymentRefundStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAtUtc = nowUtc ?? DateTime.UtcNow;
    }

    private void EnsurePending(string verb)
    {
        if (Status != PaymentRefundStatus.Pending)
        {
            throw new InvalidOperationException($"Sólo se puede {verb} una devolución en estado Pending (estado actual: {Status}).");
        }
    }
}
