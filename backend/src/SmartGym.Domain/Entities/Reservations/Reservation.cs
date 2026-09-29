using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Reservations;

public class Reservation : BaseEntity
{
    public Guid ClassSessionId { get; set; }
    public ClassSession ClassSession { get; set; } = null!;

    public Guid StudentId { get; set; }
    public Person Student { get; set; } = null!;

    public Guid? MembershipId { get; set; }
    public Membership? Membership { get; set; }

    public Guid? CreditMovementId { get; set; }
    public MembershipCreditMovement? CreditMovement { get; set; }

    public ReservationStatus Status { get; set; } = ReservationStatus.Reserved;

    public DateTime ReservedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public bool IsLateCancellation { get; set; }
    public Guid? RefundCreditMovementId { get; set; }
    public MembershipCreditMovement? RefundCreditMovement { get; set; }

    public DateTime? AttendedAtUtc { get; set; }
    public Guid? CheckedInByUserId { get; set; }
    public AttendanceSource? AttendanceSource { get; set; }

    public int? WaitListPosition { get; set; }

    public void Confirm()
    {
        Status = ReservationStatus.Confirmed;
        ConfirmedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Cancel(string reason, bool isLateCancellation, Guid? refundMovementId = null)
    {
        Status = ReservationStatus.Cancelled;
        CancellationReason = reason.Trim();
        IsLateCancellation = isLateCancellation;
        RefundCreditMovementId = refundMovementId;
        CancelledAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkAttended(AttendanceSource source, Guid? checkedInByUserId)
    {
        Status = ReservationStatus.Attended;
        AttendanceSource = source;
        CheckedInByUserId = checkedInByUserId;
        AttendedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkNoShow()
    {
        Status = ReservationStatus.NoShow;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
