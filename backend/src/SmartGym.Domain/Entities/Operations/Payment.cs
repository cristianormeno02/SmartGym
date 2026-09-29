using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Operations;

public class Payment : BaseEntity
{
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
        Status = PaymentStatus.Completed;
        if (!string.IsNullOrWhiteSpace(externalTransactionId))
        {
            ExternalTransactionId = externalTransactionId.Trim();
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
