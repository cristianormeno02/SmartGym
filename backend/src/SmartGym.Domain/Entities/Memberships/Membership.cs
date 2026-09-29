using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Memberships;

public class Membership : BaseEntity
{
    public Guid StudentId { get; set; }
    public Person Student { get; set; } = null!;

    public Guid MembershipPlanId { get; set; }
    public MembershipPlan MembershipPlan { get; set; } = null!;

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public int TotalCredits { get; set; }
    public int AvailableCredits { get; set; }

    public MembershipStatus Status { get; set; } = MembershipStatus.Active;

    public ICollection<MembershipCreditMovement> CreditMovements { get; set; } = new List<MembershipCreditMovement>();

    public bool IsValidOn(DateOnly date)
    {
        return Status == MembershipStatus.Active && date >= StartDate && date <= EndDate;
    }

    public bool HasAvailableCredit()
    {
        if (MembershipPlan != null && MembershipPlan.IsUnlimited)
        {
            return true;
        }

        return AvailableCredits > 0;
    }

    public MembershipCreditMovement DeductCredit(Guid? classSessionId, string reason, DateOnly? date = null)
    {
        if (date.HasValue && !IsValidOn(date.Value))
        {
            throw new InvalidOperationException("La membresía no está activa en la fecha indicada.");
        }

        if (!HasAvailableCredit())
        {
            throw new InvalidOperationException("No posee créditos disponibles en su membresía.");
        }

        var isUnlimited = MembershipPlan != null && MembershipPlan.IsUnlimited;
        if (!isUnlimited)
        {
            AvailableCredits--;
            if (AvailableCredits == 0)
            {
                Status = MembershipStatus.Depleted;
            }
        }

        UpdatedAtUtc = DateTime.UtcNow;

        var movement = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            MembershipId = Id,
            StudentId = StudentId,
            ClassSessionId = classSessionId,
            Type = CreditMovementType.ClassReservationDebit,
            Amount = isUnlimited ? 0 : -1,
            BalanceAfter = AvailableCredits,
            Reason = reason,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        CreditMovements.Add(movement);
        return movement;
    }

    public MembershipCreditMovement RefundCredit(Guid? classSessionId, string reason)
    {
        var isUnlimited = MembershipPlan != null && MembershipPlan.IsUnlimited;
        if (!isUnlimited)
        {
            AvailableCredits++;
            if (Status == MembershipStatus.Depleted)
            {
                Status = MembershipStatus.Active;
            }
        }

        UpdatedAtUtc = DateTime.UtcNow;

        var movement = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            MembershipId = Id,
            StudentId = StudentId,
            ClassSessionId = classSessionId,
            Type = CreditMovementType.ClassCancellationRefund,
            Amount = isUnlimited ? 0 : 1,
            BalanceAfter = AvailableCredits,
            Reason = reason,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        CreditMovements.Add(movement);
        return movement;
    }

    public MembershipCreditMovement AddCompensatoryCredit(Guid? classSessionId, string reason, Guid? performedByUserId)
    {
        AvailableCredits++;
        TotalCredits++;

        if (Status == MembershipStatus.Depleted)
        {
            Status = MembershipStatus.Active;
        }

        UpdatedAtUtc = DateTime.UtcNow;

        var movement = new MembershipCreditMovement
        {
            Id = Guid.NewGuid(),
            MembershipId = Id,
            StudentId = StudentId,
            ClassSessionId = classSessionId,
            Type = CreditMovementType.ClassSuspensionCompensation,
            Amount = 1,
            BalanceAfter = AvailableCredits,
            Reason = reason,
            PerformedByUserId = performedByUserId,
            CreatedAtUtc = DateTime.UtcNow,
            IsActive = true
        };

        CreditMovements.Add(movement);
        return movement;
    }
}
