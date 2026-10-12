using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Operations;

namespace SmartGym.Domain.Entities.Operations;

public class Payment : BaseEntity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();
    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public Guid PayerPersonId { get; set; }
    public Person PayerPerson { get; set; } = null!;

    // Un pago se aplica a lo sumo a una obligación: una membresía o una clase, nunca ambas.
    public Guid? MembershipId { get; set; }
    public Membership? Membership { get; set; }

    public Guid? ClassSessionId { get; set; }
    public ClassSession? ClassSession { get; set; }

    public decimal Amount { get; set; }
    public decimal RefundedAmount { get; set; }
    public string Currency { get; set; } = "ARS";
    public PaymentMethod Method { get; set; }
    public PaymentProvider Provider { get; set; } = PaymentProvider.Manual;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public string? ExternalTransactionId { get; set; }
    public string? ReceiptUrl { get; set; }
    public string? Notes { get; set; }
    public DateTime PaidAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? RegisteredByUserId { get; set; }
    public string? IdempotencyKey { get; set; }

    // Sólo en pagos de proveedor externo: identificador del checkout en el proveedor, URL de pago y vencimiento.
    public string? ExternalReference { get; set; }
    public string? CheckoutUrl { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }

    public uint Version { get; set; } // xmin

    public ICollection<PaymentRefund> Refunds { get; set; } = new List<PaymentRefund>();

    public decimal NetAmount => Amount - RefundedAmount;

    /// <summary>El cobro se confirmó alguna vez, aunque luego se haya devuelto en todo o en parte.</summary>
    public bool IsConfirmed => Status.IsConfirmed();

    public bool IsApplied => MembershipId.HasValue || ClassSessionId.HasValue;

    /// <summary>Lo procesa un proveedor externo: sólo se confirma con información verificada de ese proveedor.</summary>
    public bool IsExternal => Provider != PaymentProvider.Manual;

    public bool IsCheckoutExpired(DateTime nowUtc) =>
        IsExternal && Status == PaymentStatus.Pending && ExpiresAtUtc.HasValue && ExpiresAtUtc.Value <= nowUtc;

    public void Complete(string? externalTransactionId = null, DateTime? nowUtc = null)
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException($"Sólo se puede confirmar un pago en estado Pending (estado actual: {Status}).");
        }

        Status = PaymentStatus.Completed;
        if (!string.IsNullOrWhiteSpace(externalTransactionId))
        {
            ExternalTransactionId = externalTransactionId.Trim();
        }

        var now = nowUtc ?? DateTime.UtcNow;
        ConfirmedAtUtc = now;
        UpdatedAtUtc = now;

        AddDomainEvent(new PaymentCompletedEvent(
            Id,
            PayerPersonId,
            MembershipId,
            ClassSessionId,
            Amount,
            RegisteredByUserId,
            now));
        AddDomainEvent(new PaymentConfirmedNotificationEvent(Id, PayerPersonId, Amount, Currency));
    }

    /// <param name="notifyPayer">Falso cuando el pago no llegó a intentarse (no se pudo crear el checkout).</param>
    public void Fail(string? reason = null, DateTime? nowUtc = null, bool notifyPayer = true)
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException($"Sólo se puede rechazar un pago en estado Pending (estado actual: {Status}).");
        }

        Status = PaymentStatus.Failed;
        AppendNote("Rechazo", reason);
        UpdatedAtUtc = nowUtc ?? DateTime.UtcNow;

        if (notifyPayer)
        {
            AddDomainEvent(new PaymentFailedNotificationEvent(Id, PayerPersonId, Amount, Currency, reason?.Trim() ?? string.Empty));
        }
    }

    public void Cancel(string? reason = null, DateTime? nowUtc = null)
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException($"Sólo se puede cancelar un pago en estado Pending (estado actual: {Status}).");
        }

        var now = nowUtc ?? DateTime.UtcNow;
        Status = PaymentStatus.Cancelled;
        CancelledAtUtc = now;
        AppendNote("Cancelación", reason);
        UpdatedAtUtc = now;
    }

    public void ApplyToMembership(Guid membershipId, Guid? appliedByUserId, DateTime? nowUtc = null)
    {
        EnsureCanBeApplied();

        var now = nowUtc ?? DateTime.UtcNow;
        MembershipId = membershipId;
        UpdatedAtUtc = now;

        // Un pago pendiente dispara la activación al confirmarse; uno ya confirmado, al aplicarse.
        if (Status == PaymentStatus.Completed)
        {
            AddDomainEvent(new PaymentAppliedEvent(Id, PayerPersonId, membershipId, Amount, appliedByUserId, now));
        }
    }

    public void ApplyToClassSession(Guid classSessionId, DateTime? nowUtc = null)
    {
        EnsureCanBeApplied();

        ClassSessionId = classSessionId;
        UpdatedAtUtc = nowUtc ?? DateTime.UtcNow;
    }

    /// <summary>Devolución efectiva en el acto: la de un pago manual, que no depende de un proveedor.</summary>
    public PaymentRefund RegisterRefund(
        decimal amount,
        string reason,
        Guid? requestedByUserId,
        string? idempotencyKey = null,
        string? notes = null,
        DateTime? nowUtc = null)
    {
        var refund = RequestRefund(amount, reason, requestedByUserId, 0m, idempotencyKey, notes, nowUtc);
        CompleteRefund(refund, externalRefundId: null, nowUtc);
        return refund;
    }

    /// <summary>
    /// Registra una devolución pendiente de la respuesta del proveedor. No modifica el pago, pero su importe
    /// queda reservado: <paramref name="pendingRefundsAmount"/> es la suma de las otras devoluciones pendientes.
    /// </summary>
    public PaymentRefund RequestRefund(
        decimal amount,
        string reason,
        Guid? requestedByUserId,
        decimal pendingRefundsAmount,
        string? idempotencyKey = null,
        string? notes = null,
        DateTime? nowUtc = null)
    {
        if (Status != PaymentStatus.Completed && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Sólo se puede devolver un pago confirmado (estado actual: {Status}).");
        }

        if (amount <= 0)
        {
            throw new ArgumentException("El importe de la devolución debe ser superior a cero.", nameof(amount));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("El motivo de la devolución es obligatorio.", nameof(reason));
        }

        var available = NetAmount - pendingRefundsAmount;
        if (amount > available)
        {
            throw new InvalidOperationException(
                $"La devolución ({amount}) supera el importe disponible para devolver ({available}).");
        }

        var now = nowUtc ?? DateTime.UtcNow;
        var refund = new PaymentRefund
        {
            Id = Guid.NewGuid(),
            PaymentId = Id,
            Payment = this,
            Amount = amount,
            Currency = Currency,
            Reason = reason.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Status = PaymentRefundStatus.Pending,
            RequestedByUserId = requestedByUserId,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = now,
            IsActive = true
        };

        Refunds.Add(refund);
        return refund;
    }

    /// <summary>
    /// Hace efectiva una devolución pendiente: recién aquí cambia el importe devuelto y el estado del pago.
    /// </summary>
    public void CompleteRefund(PaymentRefund refund, string? externalRefundId = null, DateTime? nowUtc = null)
    {
        if (refund.PaymentId != Id)
        {
            throw new InvalidOperationException("La devolución no pertenece a este pago.");
        }

        if (Status != PaymentStatus.Completed && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Sólo se puede devolver un pago confirmado (estado actual: {Status}).");
        }

        if (refund.Amount > NetAmount)
        {
            throw new InvalidOperationException(
                $"La devolución ({refund.Amount}) supera el importe disponible para devolver ({NetAmount}).");
        }

        var now = nowUtc ?? DateTime.UtcNow;
        refund.Complete(externalRefundId, now);

        RefundedAmount += refund.Amount;
        Status = RefundedAmount == Amount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        UpdatedAtUtc = now;

        AddDomainEvent(new PaymentRefundedNotificationEvent(Id, refund.Id, PayerPersonId, refund.Amount, Currency));
    }

    private void EnsureCanBeApplied()
    {
        if (IsApplied)
        {
            throw new InvalidOperationException("El pago ya se encuentra aplicado a un concepto.");
        }

        if (Status != PaymentStatus.Pending && Status != PaymentStatus.Completed)
        {
            throw new InvalidOperationException($"No se puede aplicar un pago en estado {Status}.");
        }
    }

    private void AppendNote(string label, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        Notes = string.IsNullOrWhiteSpace(Notes) ? reason.Trim() : $"{Notes} | {label}: {reason.Trim()}";
    }
}
