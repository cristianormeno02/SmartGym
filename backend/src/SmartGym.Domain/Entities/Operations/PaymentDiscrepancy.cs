using SmartGym.Domain.Common;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Operations;

/// <summary>
/// Diferencia entre lo que informa el proveedor y el pago interno. Registrarla no modifica el pago:
/// queda abierta hasta que un administrador la revisa y deja constancia de cómo se resolvió.
/// </summary>
public class PaymentDiscrepancy : BaseEntity
{
    // Nulo cuando la transacción del proveedor no corresponde a ningún pago interno.
    public Guid? PaymentId { get; set; }
    public Payment? Payment { get; set; }

    public PaymentProvider Provider { get; set; }
    public PaymentDiscrepancyType Type { get; set; }
    public PaymentDiscrepancyStatus Status { get; set; } = PaymentDiscrepancyStatus.Open;

    public decimal? InternalAmount { get; set; }
    public string? InternalCurrency { get; set; }
    public string? InternalStatus { get; set; }

    public decimal? ExternalAmount { get; set; }
    public string? ExternalCurrency { get; set; }
    public string? ExternalStatus { get; set; }
    public string? ExternalTransactionId { get; set; }

    public DateTime DetectedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionNotes { get; set; }

    public static PaymentDiscrepancy Create(
        PaymentDiscrepancyType type,
        PaymentProvider provider,
        Payment? payment,
        string? externalTransactionId,
        decimal? externalAmount,
        string? externalCurrency,
        string? externalStatus,
        DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        return new PaymentDiscrepancy
        {
            Id = Guid.NewGuid(),
            PaymentId = payment?.Id,
            Provider = provider,
            Type = type,
            Status = PaymentDiscrepancyStatus.Open,
            InternalAmount = payment?.Amount,
            InternalCurrency = payment?.Currency,
            InternalStatus = payment?.Status.ToString(),
            ExternalAmount = externalAmount,
            ExternalCurrency = externalCurrency,
            ExternalStatus = externalStatus,
            ExternalTransactionId = externalTransactionId,
            DetectedAtUtc = now,
            CreatedAtUtc = now,
            IsActive = true
        };
    }

    public void Resolve(string notes, Guid? resolvedByUserId, DateTime? nowUtc = null)
    {
        if (Status != PaymentDiscrepancyStatus.Open)
        {
            throw new InvalidOperationException("La discrepancia ya fue resuelta.");
        }

        if (string.IsNullOrWhiteSpace(notes))
        {
            throw new ArgumentException("Las notas de resolución son obligatorias.", nameof(notes));
        }

        var now = nowUtc ?? DateTime.UtcNow;
        Status = PaymentDiscrepancyStatus.Resolved;
        ResolutionNotes = notes.Trim();
        ResolvedByUserId = resolvedByUserId;
        ResolvedAtUtc = now;
        UpdatedAtUtc = now;
    }
}
