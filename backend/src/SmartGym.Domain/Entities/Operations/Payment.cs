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

    public Guid? MembershipId { get; set; }
    public Membership? Membership { get; set; }

    public Guid? ClassSessionId { get; set; }
    public ClassSession? ClassSession { get; set; }

    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    public string? ExternalTransactionId { get; set; }
    public string? ReceiptUrl { get; set; }
    public string? Notes { get; set; }
    public DateTime PaidAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? RegisteredByUserId { get; set; }

    public void Complete(string? externalTransactionId = null)
    {
        if (Status == PaymentStatus.Failed || Status == PaymentStatus.Refunded)
        {
            throw new InvalidOperationException($"No se puede confirmar un pago en estado {Status}.");
        }

        var wasCompleted = Status == PaymentStatus.Completed;

        Status = PaymentStatus.Completed;
        if (!string.IsNullOrWhiteSpace(externalTransactionId))
        {
            ExternalTransactionId = externalTransactionId.Trim();
        }

        var now = DateTime.UtcNow;
        UpdatedAtUtc = now;

        if (!wasCompleted)
        {
            AddDomainEvent(new PaymentCompletedEvent(
                Id,
                PayerPersonId,
                MembershipId,
                ClassSessionId,
                Amount,
                RegisteredByUserId,
                now));
        }
    }

    public void Fail(string? reason = null)
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOperationException($"Sólo se puede rechazar un pago en estado Pending (estado actual: {Status}).");
        }

        Status = PaymentStatus.Failed;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            Notes = string.IsNullOrWhiteSpace(Notes) ? reason.Trim() : $"{Notes} | Rechazo: {reason.Trim()}";
        }
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Refund(string? reason = null)
    {
        Status = PaymentStatus.Refunded;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            Notes = string.IsNullOrWhiteSpace(Notes) ? reason.Trim() : $"{Notes} | Reembolso: {reason.Trim()}";
        }
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
